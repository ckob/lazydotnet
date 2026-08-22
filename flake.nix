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
        pname = "lazydotnet";
        pkgs = nixpkgs.legacyPackages.${system};
        dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
        dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
      in
      rec {
        packages.${pname} =
          let
            version = "0.10.0";
          in
          pkgs.buildDotnetModule {
            inherit
              pname
              version
              dotnet-sdk
              dotnet-runtime
              ;
            src = ./.;
            projectFile = "src/${pname}.csproj";
            testProjectFile = "tests/${pname}.UnitTests/${pname}.UnitTests.csproj";
            nugetDeps = ./nix/deps.json;
            useDotnetFromEnv = true;
            dotnetBuildFlags = [ "/p:MinVerVersion=${version}" ];
            executables = [ pname ];
            meta = with pkgs.lib; {
              description = "Terminal UI for .NET solutions, inspired by lazygit";
              homepage = "https://github.com/ckob/${pname}";
              license = licenses.mit;
              mainProgram = pname;
              platforms = platforms.unix;
            };
          };

        packages.default = packages.${pname};

        apps = {
          ${pname} = flake-utils.lib.mkApp {
            drv = packages.${pname};
            name = pname;
          };
          default = apps.${pname};
          fetch-deps = {
            type = "app";
            program = "${packages.${pname}.fetch-deps}";
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
