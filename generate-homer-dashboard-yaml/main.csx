#!/usr/bin/env dotnet-script

#r "nuget: Docker.DotNet, 3.125.4"
#r "nuget: YamlDotNet, 11.2.1"

using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.EventEmitters;

public static Regex HostPathPrefixRegex = new Regex(
    "Host\\(`(?<host>[^`]*)`\\).*PathPrefix\\(`(?<path>[^`]*)`\\)",
    RegexOptions.CultureInvariant
    | RegexOptions.Compiled
);

public static Regex HostRegex = new Regex(
    "Host\\(`(?<host>[^`]*)`\\)",
    RegexOptions.CultureInvariant
    | RegexOptions.Compiled
);

/*
title: "Demo dashboard"
subtitle: "Homer"
logo: "logo.png"

services:
  - name: "Applications"
    icon: "fas fa-cloud"
    items:
      - name: "Awesome app"
        logo: "assets/tools/sample.png"
        subtitle: "Bookmark example"
        tag: "app"
        url: "https://www.reddit.com/r/selfhosted/"
        target: "_blank" # optional html a tag target attribute
*/

public record Dashboard(string Title, string Subtitle, string Logo, List<Category> Services);
public record Category(string Name, string Icon, List<Item> Items);
public record Item(string Name, string Tag, string Url, string Target = "_blank", string Logo = "assets/tools/sample.png", string Subtitle = "");

DockerClient client = new DockerClientConfiguration(
        new Uri("unix:///var/run/docker.sock"))
    .CreateClient();
// Win: npipe://./pipe/docker_engine

var containers = await client.Containers.ListContainersAsync(new ContainersListParameters());

const string TraefikEnable = "traefik.enable";
const string TraefikRouterStart = "traefik.http.routers";
const string TraefikRouterEnd= ".rule";
const string CategoryLabel= "ch.freaxnx01.category";
const string TitleLabel= "ch.freaxnx01.title";

var dashboard = new Dashboard(
    Title: "Dashboard",
    Subtitle: "",
    Logo: "logo.png",
    Services: new List<Category>()
);

foreach (var container in containers)
{
    if (container.Labels.ContainsKey(TraefikEnable) && container.Labels[TraefikEnable] == "true")
    {
        var categoryName = string.Empty;

        // Category
        var categoryLabel = container.Labels.SingleOrDefault(l => l.Key == CategoryLabel);
        if (!categoryLabel.Equals(default(KeyValuePair<string, string>)))
        {
            categoryName = categoryLabel.Value;
        }

        var category = dashboard.Services.SingleOrDefault(c => c.Name == categoryName);

        if (category is null)
        {
            category = new Category(
                Name: categoryName,
                Icon: "fas fa-cloud",
                Items: new List<Item>()
            );
            dashboard.Services.Add(category);
        }

        // Title
        var title = string.Empty;
        var titleLabel = container.Labels.SingleOrDefault(l => l.Key == TitleLabel);
        if (!titleLabel.Equals(default(KeyValuePair<string, string>)))
        {
            title = titleLabel.Value;
        }
        
        var label = container.Labels.SingleOrDefault(
            l => l.Key.StartsWith(TraefikRouterStart) && l.Key.EndsWith(TraefikRouterEnd));
        if (!label.Equals(default(KeyValuePair<string, string>)))
        {
            var rule = label.Value;
            var match = HostPathPrefixRegex.Match(rule);
            
            if (string.IsNullOrEmpty(match.Groups["path"].Value))
            {
                 match = HostRegex.Match(rule);
            }

            var item = new Item(
                Name: title, 
                Tag: container.Names[0].Substring(1).FirstCharToUpper(),
                Url: $"https://{match.Groups["host"].Value}{match.Groups["path"].Value}"
            );

            category.Items.Add(item);
        }
    }
}

var serializer = new SerializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .WithIndentedSequences()
    .WithEventEmitter(nextEmitter => new QuoteSurroundingEventEmitter(nextEmitter))
    .Build();

var yaml = serializer.Serialize(dashboard);
Console.Write(yaml);

public static void Dump(this object text)
{
    Console.WriteLine(text);
}

public static string FirstCharToUpper(this string input) =>
    input switch
    {
        null => throw new ArgumentNullException(nameof(input)),
        "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
        _ => input.First().ToString().ToUpper() + input.Substring(1)
    };

public class QuoteSurroundingEventEmitter : ChainedEventEmitter
{
    public QuoteSurroundingEventEmitter(IEventEmitter nextEmitter)  : base(nextEmitter)
    { }

    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if(eventInfo.Source.StaticType == typeof (String))
            eventInfo.Style = ScalarStyle.DoubleQuoted;
            base.Emit(eventInfo, emitter);
    }
}