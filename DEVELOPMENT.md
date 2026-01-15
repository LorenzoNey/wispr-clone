# Development Instructions

## .NET SDK Location

The .NET SDK is installed in the user directory, not the system directory:

```
C:\Users\LaurentiuNae\.dotnet\dotnet.exe
```

**Version:** 8.0.416

## Building the Project

```bash
cd "D:\BA Work\wispr-clone\src\WisprClone.Avalonia"
"C:\Users\LaurentiuNae\.dotnet\dotnet.exe" build --configuration Debug
```

## Running the Project

```bash
cd "D:\BA Work\wispr-clone\src\WisprClone.Avalonia"
"C:\Users\LaurentiuNae\.dotnet\dotnet.exe" run --configuration Debug
```

## Publishing for Release

### Windows x64
```bash
"C:\Users\LaurentiuNae\.dotnet\dotnet.exe" publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Windows ARM64
```bash
"C:\Users\LaurentiuNae\.dotnet\dotnet.exe" publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```

## Note

The system `dotnet.exe` at `C:\Program Files\dotnet\dotnet.exe` only has the runtime installed, not the SDK. Always use the user-installed SDK path for building.
