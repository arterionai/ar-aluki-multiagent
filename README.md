# ar-aluki-multiagent

Initial implementation bootstrap for the Aluki runtime.

## Current runnable baseline

- Solution: Aluki.Runtime.slnx
- Contracts library: src/Aluki.Runtime.Abstractions
- Runtime host: src/Aluki.Runtime.Host

## Build and run

1. dotnet restore Aluki.Runtime.slnx
2. dotnet build Aluki.Runtime.slnx
3. dotnet run --project src/Aluki.Runtime.Host/Aluki.Runtime.Host.csproj

