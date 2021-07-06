#!/usr/bin/env dotnet-script

#r "nuget: Docker.DotNet, 3.125.4"
#r "nuget: YamlDotNet, 11.2.1"

using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
public record Item(string Name, string Tag, string Url);
public record Category(string Name, List<Item> Items);

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

var entries = new List<Item>();

var services = new List<Category>();

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

        var category = services.SingleOrDefault(c => c.Name == categoryName);

        if (category is null)
        {
            category = new Category(Name: categoryName, Items: new List<Item>());
            services.Add(category);
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
            match = HostRegex.Match(rule);
            
            if (!match.Groups.ContainsKey("path"))
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
    .Build();
var yaml = serializer.Serialize(services);
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
