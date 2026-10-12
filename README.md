# Universal YAML Parser for Unity

## Choose a reader

- **`DesktopYamlReader`** reads files directly from the local `StreamingAssets`
  folder. Use it for Windows, macOS, or Linux desktop builds.
- **`MobileYamlReader`** reads files with Unity `UnityWebRequest`. Use it for
  Android, iOS, or WebGL. It also works on desktop.

Both readers use the same parser and support the same YAML format, ID
variations, and loading methods. There is no `UniversalYamlReader` class.
Shared parsing code is an internal helper included in `DesktopYamlReader.cs`.

`MobileYamlReader` needs the Unity `UnityWebRequest` module. Add this module to
the Unity project to compile and use the mobile/WebGL reader.

## What it does

This is a small YAML key-value parser for Unity. It works with IDs and text
from any game or project. It does not need to know what a game, notification,
character, or message is. It reads each key and its text and returns them in a
dictionary.

This parser supports a simple part of YAML. It is not a full YAML parser. Each
value must be a double-quoted string:

```yaml
menu.start: "Start game"
menu.exit: "Exit"
dialogue.hello: "Hello, {player}!"
dialogue.hello_1: "Hi, {player}!"
dialogue.hello_9: "Good to see you, {player}!"
```

Placeholders such as `{player}` and rich-text tags stay as plain text. The
parser does not replace or interpret them.

## Add to a Unity project

Copy both `DesktopYamlReader.cs` and `MobileYamlReader.cs` from the `Scripts`
folder into your Unity project. The shared parser is inside
`DesktopYamlReader.cs`, so both files are needed to compile either reader.
The Unity `UnityWebRequest` module is required for `MobileYamlReader`.

To load files, put them in `Assets/StreamingAssets`.

## Create a test file and print a value

First, create this file:

`Assets/StreamingAssets/Test.yaml`

```yaml
menu.start: "Start game"
```

Then create a C# script named `YamlSmokeTest.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public class YamlSmokeTest : MonoBehaviour
{
    private async void Start()
    {
        try
        {
            Dictionary<string, string> values =
                await DesktopYamlReader.LoadAsync("Test.yaml");

            string startText;
            if (values.TryGetValue("menu.start", out startText))
            {
                Debug.Log("Start text: " + startText);
            }
            else
            {
                Debug.LogError("The key 'menu.start' was not found.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("Could not load Test.yaml: " + exception);
        }
    }
}
```

Add this component to an active GameObject and enter Play Mode. The Console
should show `Start text: Start game`.

Use `MobileYamlReader.LoadAsync("Test.yaml")` instead when testing on Android,
iOS, or WebGL. The file path is relative to `Assets/StreamingAssets`, and the
path spelling and letter case must match the file.

## Parse text already in memory

```csharp
Dictionary<string, string> values =
    DesktopYamlReader.Parse("menu.start: \"Start game\"");
string startText = values["menu.start"];
```

`MobileYamlReader.Parse` works the same way. Parsing an in-memory string does
not access the file system or make a web request.

## Get text variations

Add an underscore and a positive number to an ID to make a variation. Numbers
do not need to be continuous. `GetVariations` returns the base text first, if
it exists, and then all numbered variations in numeric order:

```csharp
List<string> greetings =
    DesktopYamlReader.GetVariations(values, "dialogue.hello");
```

For the example above, the result has the base text, `hello_1`, and `hello_9`.
A missing `hello_2` does not stop the search. `MobileYamlReader` provides the
same method.

To choose one text at random:

```csharp
string greeting =
    DesktopYamlReader.GetRandomVariation(values, "dialogue.hello");
```

`GetRandomVariation` returns `null` if the ID has no text or variations.

## Supported format

- One key and one double-quoted string per line.
- Blank lines and lines starting with `#` are ignored.
- Newline (`\n`), quote (`\"`), and backslash (`\\`) escapes are supported.
- Duplicate keys keep the first value and log a warning.
- Invalid lines are skipped and log a warning.
- Files use UTF-8.

Android and WebGL cannot reliably list files in `StreamingAssets`. Pass file
paths explicitly when loading more than one file with `LoadManyAsync`.
