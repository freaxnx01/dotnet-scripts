#!/usr/bin/env dotnet-script

#r "nuget: Docker.DotNet, 3.125.4"
#r "nuget: YamlDotNet, 11.2.1"

using System.Collections;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.EventEmitters;

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

public class Dashboard
{
    public string Title { get; set; }
    public string Subtitle { get; set; }
    public string Logo { get; set; }
    public List<Service> Services { get; set; }

    public Dashboard()
    {
        Services = new List<Service>();
    }
}

public class Service
{
    public string Name { get; set; }
    public string Icon { get; set; }
    public List<Item> Items { get; set; }

    public Service()
    {
        Items = new List<Item>();
    }
}

public class Item
{
    public string Name { get; set; }
    public string Tag { get; set; }
    public string Url { get; set; }
    public string Target { get; set; }
    public string Logo { get; set; }
    public string Subtitle { get; set; }

    public Item()
    {
        Target = "_blank";
        Logo = "assets/tools/sample.png";
    }
}

var containers = await GetDockerClient().Containers.ListContainersAsync(new ContainersListParameters());

// Docker labels

/*
"ch.freaxnx01.dashboard-title="

"ch.freaxnx01.category=Cloud"
"ch.freaxnx01.title=Nextcloud"

"ch.freaxnx01.category=web.freaxnx01.ch"
"ch.freaxnx01.title=Movies"

"ch.freaxnx01.path.switzerland.title=Schweiz"
"ch.freaxnx01.path.switzerland.target=..."
*/

const string BaseLabel = "ch.freaxnx01.";
const string DashboardTitleLabel = BaseLabel + "dashboard-title";
const string CategoryLabel = BaseLabel + "category";
const string TitleLabel = BaseLabel + "title";
const string PathLabel= BaseLabel + "path";

const string TraefikEnable = "traefik.enable";
const string TraefikRouterStart = "traefik.http.routers";
const string TraefikRouterEnd= ".rule";

Dashboard dashboard;

var relevantContainers = containers.Where(c => c.LabelNamed(TraefikEnable) == "true");

// Get Traefik Container, create Dashboard
foreach (var container in relevantContainers)
{
    if (container.LabelExists(DashboardTitleLabel))
    {
        dashboard = new Dashboard()
        {
            Title = container.LabelNamed(DashboardTitleLabel),
            Subtitle = "",
            Logo = "logo.png"
        };
    }
}

foreach (var container in relevantContainers)
{
    // Service
    var serviceName = container.LabelNamed(CategoryLabel);

    var service = dashboard.Services.SingleOrDefault(c => c.Name == serviceName);

    var title = container.LabelNamed(TitleLabel);

    // .path
    var hasPathLabels = container.Labels.Any(l => l.Key.StartsWith(PathLabel));

    if (hasPathLabels)
    {
        serviceName += $" {title}";
    }

    if (service is null)
    {
        service = new Service()
        {
            Name = serviceName,
            Icon = "fas fa-cloud",
        };
        
        dashboard.Services.Add(service);
    }

    var rule = container.LabelNamed(TraefikRouterStart, TraefikRouterEnd);
    if (rule != null)
    {
        (string host, string path) = ParseTraefikRule(rule);

        var baseUrl = $"https://{host}{path}/";
        var containerName = container.Names[0].Substring(1).FirstCharToUpper();

        if (hasPathLabels)
        {
            /*
            - "ch.freaxnx01.path.switzerland.title=Schweiz"
            - "ch.freaxnx01.path.switzerland.target=..."
            */

            foreach (var subLabel in GetSubLabels(PathLabel, container.Labels))
            {
                var item = new Item()
                {
                    Name = subLabel.Value["title"], 
                    Tag = $"{containerName} {subLabel.Key}",
                    Url = string.Concat(baseUrl, subLabel.Value["target"])
                };

                service.Items.Add(item);
            }
        }
        else
        {
            var item = new Item()
            {
                Name = title, 
                Tag = containerName,
                Url = baseUrl
            };

            service.Items.Add(item);
        }
    }
}

var dashboardSorted = new Dashboard()
{
    Title = dashboard.Title,
    Subtitle = dashboard.Subtitle,
    Logo = dashboard.Logo,
    Services = dashboard.Services.OrderBy(s => s.Name).ToList()
};

foreach (var service in dashboardSorted.Services)
{
    service.Items = service.Items.OrderBy(s => s.Name).ToList();
}

Console.Write(dashboardSorted.SerialzeToYaml());

//TODO -> DockerApiClient
public Dictionary<string, Dictionary<string, string>> GetSubLabels(string subLabelName, IDictionary<string, string> labels)
{
    /*
    Input:
    - "ch.freaxnx01.path.switzerland.title=Schweiz"
    - "ch.freaxnx01.path.switzerland.target=..."
    */

    /*
    Output:
    - switzerland
        - title=Schweiz
        - target=...
    */

    var dict = new Dictionary<string, Dictionary<string, string>>();

    var labelTag = string.Empty;

    foreach (var subLabel in labels.Where(l => l.Key.StartsWith(subLabelName)))
    {
        var subLabelKey = subLabel.Key.Replace(subLabelName + ".", string.Empty);

        var splitValues = subLabelKey.Split('.');
        if (splitValues.Count() == 2)
        {
            // switzerland.title
            labelTag = splitValues[0];
            var key = splitValues[1];

            if (!dict.ContainsKey(labelTag))
            {
                dict.Add(labelTag, new Dictionary<string, string>());
            }

            dict[labelTag].Add(key, subLabel.Value);
        }
    }

    return dict;
}

#region Parse Traefik rule
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

public static (string Host, string Path) ParseTraefikRule(string rule)
{
    var match = HostPathPrefixRegex.Match(rule);
    
    // Subdomain only
    if (string.IsNullOrEmpty(match.Groups["path"].Value))
    {
        match = HostRegex.Match(rule);
    }

    return (match.Groups["host"].Value, match.Groups["path"].Value);
}

#endregion

#region Docker client helpers

public static DockerClient GetDockerClient()
{
    return new DockerClientConfiguration(
        new Uri(IsLinux() ? "unix:///var/run/docker.sock" : "npipe://./pipe/docker_engine"))
    .CreateClient();
}

public static string LabelNamed(this ContainerListResponse container, string startsWith, string endsWith)
{
    var label = container.Labels.SingleOrDefault(l => l.Key.StartsWith(startsWith) && l.Key.EndsWith(endsWith));
    if (!label.Equals(default(KeyValuePair<string, string>)))
    {
        return label.Value;
    }

    return null;
}

public static bool LabelExists(this ContainerListResponse container, string labelName)
{
    return container.Labels.Any(l => l.Key == labelName);
}

public static string LabelNamed(this ContainerListResponse container, string labelName)
{
    var label = container.Labels.SingleOrDefault(l => l.Key == labelName);
    if (!label.Equals(default(KeyValuePair<string, string>)))
    {
        return label.Value;
    }

    return null;
}

#endregion

#region Generic helpers
public static void Dump(this object text)
{
    Console.WriteLine(text);
}

public static bool IsWindows() =>
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows);


public static bool IsLinux() =>
    RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

public static string SerialzeToYaml(this object objectToSerialize)
{
    var serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithIndentedSequences()
        .Build();

    return serializer.Serialize(objectToSerialize);
}

public static string FirstCharToUpper(this string input) =>
    input switch
    {
        null => throw new ArgumentNullException(nameof(input)),
        "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
        _ => input.First().ToString().ToUpper() + input.Substring(1)
    };

#endregion