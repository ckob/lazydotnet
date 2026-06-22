{
  description = "lazydotnet - terminal UI for .NET solutions, inspired by lazygit";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};

        # Prefer .NET 10 SDK if available in nixpkgs, fallback to 9
        dotnet-sdk =
          if pkgs.dotnetCorePackages ? sdk_10_0
          then pkgs.dotnetCorePackages.sdk_10_0
          else pkgs.dotnetCorePackages.sdk_9_0;

        dotnet-runtime =
          if pkgs.dotnetCorePackages ? runtime_10_0
          then pkgs.dotnetCorePackages.runtime_10_0
          else pkgs.dotnetCorePackages.runtime_9_0;
      in
      rec {
        packages.lazydotnet = pkgs.buildDotnetModule {
          pname = "lazydotnet";
          version = "0.1.0";

          src = ./.;

          projectFile = "src/lazydotnet.csproj";
          testProjectFile = "tests/lazydotnet.UnitTests/lazydotnet.UnitTests.csproj";

          # Regenerate via: nix run .#fetch-deps
          nugetDeps = ./nix/deps.json;

          inherit dotnet-sdk dotnet-runtime;

          # git is required by MinVer to determine the version from tags
          nativeBuildInputs = [ pkgs.git ];

          executables = [ "lazydotnet" ];

          # Skip integration tests at build time (require external test binaries)
          doCheck = true;

          meta = with pkgs.lib; {
            description = "Terminal UI for .NET solutions, inspired by lazygit";
            homepage = "https://github.com/ckob/lazydotnet";
            license = licenses.mit;
            mainProgram = "lazydotnet";
            platforms = platforms.unix;
            maintainers = [ ];
          };
        };

        packages.default = packages.lazydotnet;

        apps.lazydotnet = flake-utils.lib.mkApp {
          drv = packages.lazydotnet;
          name = "lazydotnet";
        };
        apps.default = apps.lazydotnet;

        apps.fetch-deps = {
          type = "app";
          program = "${packages.lazydotnet.fetch-deps}";
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
      });
}
