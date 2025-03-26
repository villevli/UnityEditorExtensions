# Unity Editor Extensions

Collection of small editor extensions, tools and tweaks.

- [DefaultAssetInspector](Editor/DefaultAssetInspector.cs) - Shows the content of unsupported files in the inspector
- [GameViewObjectPicker](Editor/GameViewObjectPicker.cs) - Left click to select the object that is visible under the cursor in game view. If in play mode it needs to be paused


## Installation

Most of the scripts here are self contained so if you want to pick something just copy that script to your project.

Or you can install everything via Package Manager by adding the following line to *Packages/manifest.json*:
- `"com.villevli.editor-extensions": "https://github.com/villevli/UnityEditorExtensions.git"`


## Development

Clone this package into `Packages/com.villevli.editor-extensions` in a Unity project.
For now development should be done in Unity 2021.3 to preserve backwards compability. Test in latest Unity versions separately.

See the `TestAssets/` folder for scenes and assets to test the different scripts.

To test in different Unity versions easily, create a separate project for each version and create a symbolic link in the Packages folder. Any changes you make will then appear in all projects automatically.

Example for Windows to create a link in UnityPackageTestProject6000 that points to UnityPackageTestProject. Run cmd as admin
```bat
mklink /D "W:\villevli\UnityPackageTestProject6000\Packages\com.villevli.editor-extensions" "W:\villevli\UnityPackageTestProject\Packages\com.villevli.editor-extensions"
```
