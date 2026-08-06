# Shared Avalonia presentation migration

## Objective

GraveOps uses one shared Avalonia presentation layer for Linux, Windows and
future macOS desktop hosts. Desktop projects are composition roots. Native and
remote mechanics remain in platform providers.

## Exact reference source

- Linux visual and functional source: `8699e7628196d80f6fee111e77bc4f12fae6e229`
- Reviewed Windows provider and telemetry parent: `68f5c2888c0025a4a7bb28e6880ea187b940c65e`
- Shared presentation project:
  `src/GraveOps.Presentation.Avalonia`

## Classification

| Area | Status |
|---|---|
| Shared presentation assembly | shared/completed foundation |
| Exact Linux source preserved in unified history | shared/completed |
| Windows provider and telemetry work preserved | shared/completed |
| Unified dashboard extraction | pending port |
| Shared shell/navigation extraction | pending port |
| Shared media workspaces | pending port |
| Linux desktop consumption of shared views | pending port |
| Windows desktop consumption of shared views | pending port |
| Native Windows operations matching Linux capabilities | pending port |
| Remote Linux from Windows | pending port |
| Remote Windows from Linux | pending port |
| macOS composition/provider | pending port |

## Non-negotiable acceptance

A presentation feature is not complete until Linux and Windows consume the
same shared view/control and runtime screenshots show the same composition.
A functional feature is not complete until applicable native and remote
provider paths have explicit capability and runtime evidence.