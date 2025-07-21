# ViceMCP v0.9.0 Release Notes

This release of ViceMCP introduces initial support for the VICE Commodore emulator, including tools for interacting with the emulator's Model Context Protocol (MCP).

## Features

🎉 Implemented basic MCP client functionality to connect to and communicate with the VICE emulator.
- Ability to start, stop, and control the emulator lifecycle.
- Read and write emulator state information using the MCP protocol.
- Experimental support for common emulator operations like loading disk images.

## Improvements

💪 Improved overall project structure and code organization to support future growth and extensibility.
- Adopted a modular design with clear separation of concerns.
- Introduced a fluent API for a more intuitive developer experience.

## Breaking Changes

⚠️ This release introduces a breaking change to the `ViceEmulatorClient` constructor. The previous constructor has been deprecated in favor of a new constructor that requires a `ViceEmulatorOptions` object for configuration.

## Developer Notes

- The `ViceEmulatorClient` class is the main entry point for interacting with the VICE emulator via MCP.
- Extensive logging and error handling have been implemented to aid in debugging.
- Documentation and samples will be provided in a future release to help developers get started.

## What's Next

In the next release, we plan to focus on expanding the MCP feature set, including support for more advanced emulator operations and integrations with external tools or services.