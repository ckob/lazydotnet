{
  description = "lazydotnet - terminal UI for .NET solutions, inspired by lazygit";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs =
    {
      self,
      nixpkgs,
      flake-utils,
    }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
        dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
        dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
      in
      rec {
        packages.lazydotnet =
          let
            version = "0.8.1";
          in
          pkgs.buildDotnetModule {
            inherit version dotnet-sdk dotnet-runtime;
            pname = "lazydotnet";
            src = ./.;
            projectFile = "src/lazydotnet.csproj";
            testProjectFile = "tests/lazydotnet.UnitTests/lazydotnet.UnitTests.csproj";
            nugetDeps = ./nix/deps.json;
            useDotnetFromEnv = true;
            dotnetBuildFlags = [ "/p:MinVerVersion=${version}" ];
            executables = [ "lazydotnet" ];
            meta = with pkgs.lib; {
              description = "Terminal UI for .NET solutions, inspired by lazygit";
              homepage = "https://github.com/ckob/lazydotnet";
              license = licenses.mit;
              mainProgram = "lazydotnet";
              platforms = platforms.unix;
            };
          };

        packages.default = packages.lazydotnet;

        apps = {
          lazydotnet = flake-utils.lib.mkApp {
            drv = packages.lazydotnet;
            name = "lazydotnet";
          };
          default = apps.lazydotnet;
          fetch-deps = {
            type = "app";
            program = "${packages.lazydotnet.fetch-deps}";
          };
        };

        devShells.default = pkgs.mkShell {
          packages = [
            dotnet-sdk
            pkgs.nuget-to-json
          ];

          shellHook = ''
            export DOTNET_ROOT=${dotnet-sdk}
            export DOTNET_CLI_TELEMETRY_OPTOUT=1
            export DOTNET_NOLOGO=1
          '';
        };

        formatter = pkgs.nixpkgs-fmt;
      }
    );
}
