# Nix packaging

This directory contains the Nix dependency lockfile for `buildDotnetModule`.

## Install

### From flake (recommended)

```bash
nix profile install github:ckob/lazydotnet
```

Or run without installing:

```bash
nix run github:ckob/lazydotnet
```

### Local build

```bash
nix build
./result/bin/lazydotnet
```

## Development shell

```bash
nix develop
dotnet build src/lazydotnet.csproj
```

The shell exports `DOTNET_ROOT` and disables telemetry.

## Regenerating `deps.json`

After changing any `PackageReference` in `src/lazydotnet.csproj` or the test
project, regenerate the NuGet lockfile so Nix can fetch packages in offline
sandbox mode:

```bash
nix run .#fetch-deps -- nix/deps.json
```

Then commit `nix/deps.json`.

## Notes

- The flake prefers `dotnet-sdk_10_0` from nixpkgs and falls back to 9.0 if 10
  is not yet packaged in the channel you track.
- The package is a global tool (`PackAsTool=true`). The flake outputs a single
  executable at `$out/bin/lazydotnet`.
