# Research Sources and Adaptation Notes

This pack is original GraveOps-specific instruction text. It was informed by, but does not copy, the following public primary sources:

- Matt Pocock, `mattpocock/skills`: feedback loops, test-first implementation, disciplined bug diagnosis, architecture vocabulary, and code review.
  - https://github.com/mattpocock/skills
- .NET team, `dotnet/skills`: narrow .NET, MSBuild, package, diagnostics, and testing specialist skills.
  - https://github.com/dotnet/skills
- Microsoft MSBuild documentation: diagnostic verbosity and binary logs.
  - https://learn.microsoft.com/visualstudio/msbuild/obtaining-build-logs-with-msbuild
- Avalonia documentation: headless testing and platform deployment.
  - https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform
  - https://docs.avaloniaui.net/docs/deployment/macos/

The design also incorporates the project-specific rules recorded in the GraveOps Notion knowledge base, particularly the cross-platform parity ledger and the known-issues/lessons page.

## Selection rationale

- Public engineering skills were selected for their feedback loops and reproducible workflows, not popularity alone.
- Official .NET skills were kept as narrow specialists because GraveOps is a .NET application, but they do not address Avalonia parity, Linux providers, macOS packaging, privileged operations, or GraveOps release discipline by themselves.
- A custom cross-platform Avalonia skill was necessary because the project has explicit shared-layer, provider, runtime-XAML, parity-ledger, and platform-release requirements.
- The former public `caveman` skill was not adopted as an engineering method. Compressed communication can still be used when requested, but it must never remove evidence, caveats, commands, or safety checks.
