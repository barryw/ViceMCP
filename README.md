<div align="center">

![ViceMCP Logo](Images/vicemcp-logo.svg)

[![.NET](https://img.shields.io/badge/.NET-9.0+-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20|%20macOS%20|%20Windows-blue.svg)](https://github.com/barryw/ViceMCP)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/barryw/ViceMCP)](https://github.com/barryw/ViceMCP/releases/latest)
[![CI/CD](https://github.com/barryw/ViceMCP/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/barryw/ViceMCP/actions/workflows/ci-cd.yml)
[![Coverage](https://codecov.io/gh/barryw/ViceMCP/branch/main/graph/badge.svg)](https://codecov.io/gh/barryw/ViceMCP)
[![MCP](https://img.shields.io/badge/MCP-Compatible-orange.svg)](https://modelcontextprotocol.io/)

**AI-Powered Commodore Development Bridge**

[MCP Tools Reference](#-mcp-tools-reference) • [Quick Start](#-quick-start) • [Examples](#-examples)

</div>

## Overview

ViceMCP bridges the gap between modern AI assistants and retro computing by exposing the VICE Commodore emulator's powerful debugging capabilities through the Model Context Protocol (MCP). This enables AI assistants like Claude to directly interact with running Commodore 64, VIC-20, PET, and other 8-bit Commodore computer emulations.

### ✨ Key Features

- 🤖 **AI Integration** - Use Claude or other MCP clients to debug Commodore programs
- 🔍 **Memory Operations** - Read, write, search, and analyze memory in real-time
- 🐛 **Advanced Debugging** - Set breakpoints, step through code, examine registers
- ⚡ **Batch Commands** - Execute multiple operations in one shot for 10x performance
- 📦 **Zero Dependencies** - Native AOT builds available - no .NET runtime needed!
- 🚀 **Cross-Platform** - Works on Windows, macOS, and Linux
- 🎮 **Multi-Machine Support** - C64, C128, VIC-20, PET, Plus/4, and more

### ⚡ Batch Commands - 10x Performance Boost!

Execute multiple VICE operations in a single command for massive performance gains:

```json
execute_batch [
  {"command": "write_memory", "parameters": {"startHex": "0xD020", "dataHex": "00"}},
  {"command": "write_memory", "parameters": {"startHex": "0xD021", "dataHex": "05"}},
  {"command": "fill_memory", "parameters": {"startHex": "0x0400", "endHex": "0x07E7", "pattern": "A0"}},
  {"command": "write_memory", "parameters": {"startHex": "0x0400", "dataHex": "08 05 0C 0C 0F"}}
]
```

**Result**: Set border/background colors, clear screen, and write "HELLO" in ~1.4 seconds instead of ~14 seconds! 

Check out the [batch_examples/](batch_examples/) directory for ready-to-use JSON files.

## Installation

### 📦 Download Release

Download the latest release for your platform from the [releases page](https://github.com/barryw/ViceMCP/releases/latest).

**Native AOT Builds Available!** 🚀 No .NET runtime required - just download and run:
- `vicemcp-linux-x64` - Linux x64 native executable
- `vicemcp-windows-x64` - Windows x64 native executable
- `vicemcp-macos-x64` - macOS Intel native executable
- `vicemcp-macos-arm64` - macOS Apple Silicon native executable

### 🐳 Docker

#### Quick Test
```bash
docker run -it ghcr.io/barryw/vicemcp:latest
```

#### Using with Claude Code CLI
```bash
# Using Docker image directly
claude mcp add vicemcp "docker run -i --rm ghcr.io/barryw/vicemcp:latest"

# With VICE binary path mounted (if you have VICE installed locally)
claude mcp add vicemcp "docker run -i --rm -v /usr/local/bin:/vice:ro ghcr.io/barryw/vicemcp:latest" --env VICE_BIN_PATH=/vice
```

#### Manual Configuration for Docker
```json
{
  "mcpServers": {
    "vicemcp": {
      "command": "docker",
      "args": ["run", "-i", "--rm", "ghcr.io/barryw/vicemcp:latest"],
      "env": {
        "VICE_MONITOR_PORT": "6502"
      }
    }
  }
}
```

#### Docker Compose Setup
```yaml
# docker-compose.yml
version: '3.8'
services:
  vicemcp:
    image: ghcr.io/barryw/vicemcp:latest
    stdin_open: true
    tty: true
    environment:
      - VICE_MONITOR_PORT=6502
      - VICE_STARTUP_TIMEOUT=5000
```

Then configure with:
```bash
claude mcp add vicemcp "docker-compose run --rm vicemcp"
```

### 🔧 Build from Source

```bash
git clone https://github.com/barryw/ViceMCP.git
cd ViceMCP
dotnet build
```

## Quick Start

### 1️⃣ Start VICE with Binary Monitor

```bash
x64sc -binarymonitor -binarymonitoraddress 127.0.0.1:6502
```

### 2️⃣ Configure your MCP Client

#### Using Claude Code CLI (Recommended)

```bash
# If you downloaded the release binary
claude mcp add vicemcp ~/Downloads/vicemcp-osx-arm64/ViceMCP

# Or if you built from source
claude mcp add vicemcp ~/RiderProjects/ViceMCP/ViceMCP/bin/Release/net9.0/ViceMCP

# Or using Docker (no installation needed!)
claude mcp add vicemcp "docker run -i --rm ghcr.io/barryw/vicemcp:latest"
```

With VICE path configured (if not in system PATH):

```bash
claude mcp add vicemcp ~/Downloads/vicemcp-osx-arm64/ViceMCP --env VICE_BIN_PATH=/Applications/vice-x86-64-gtk3-3.8
```

#### Manual Configuration

Add to your Claude Desktop or other MCP client configuration:

```json
{
  "mcpServers": {
    "vicemcp": {
      "command": "/path/to/vicemcp",
      "env": {
        "VICE_BIN_PATH": "/path/to/vice/bin"
      }
    }
  }
}
```

### 3️⃣ Start Debugging with AI

Ask your AI assistant to:
- "Read memory from $C000 to $C100"
- "Set a breakpoint at $0810"
- "Show me the current CPU registers"
- "Find all JSR $FFD2 instructions in memory"

## 📚 MCP Tools Reference

<details>
<summary><b>Memory Operations</b> (click to expand)</summary>

### `read_memory`
Read bytes from memory and display in hex format.
```yaml
Parameters:
  - startHex: Start address (e.g., "0x0400" or "0400")
  - endHex: End address
Returns: Hex string like "08-05-0C-0C-0F"
```

### `write_memory`
Write bytes to memory.
```yaml
Parameters:
  - startHex: Start address
  - dataHex: Space-separated hex bytes (e.g., "A9 00 8D 20 D0")
Returns: Confirmation with bytes written
```

### `copy_memory`
Copy memory from one location to another.
```yaml
Parameters:
  - sourceHex: Source start address
  - destHex: Destination start address
  - length: Number of bytes to copy
Returns: Confirmation of copy operation
```

### `fill_memory`
Fill memory region with a byte pattern.
```yaml
Parameters:
  - startHex: Start address
  - endHex: End address
  - pattern: Hex bytes to repeat (e.g., "AA 55")
Returns: Confirmation with pattern used
```

### `search_memory`
Search for byte patterns in memory.
```yaml
Parameters:
  - startHex: Search start address
  - endHex: Search end address
  - pattern: Hex bytes to find (e.g., "A9 00" for LDA #$00)
  - maxResults: Maximum matches to return (default: 10)
Returns: List of addresses where pattern found
```

### `compare_memory`
Compare two memory regions.
```yaml
Parameters:
  - addr1Hex: First region start
  - addr2Hex: Second region start
  - length: Bytes to compare
Returns: List of differences or "regions identical"
```

</details>

<details>
<summary><b>CPU Control</b> (click to expand)</summary>

### `get_registers`
Get all CPU register values.
```yaml
Returns: A, X, Y, PC, SP, and status flags
```

### `set_register`
Set a CPU register value.
```yaml
Parameters:
  - registerName: Register name (A, X, Y, PC, SP)
  - valueHex: New value in hex
Returns: Confirmation of register update
```

### `step`
Step CPU by one or more instructions.
```yaml
Parameters:
  - count: Instructions to step (default: 1)
  - stepOver: Step over subroutines (default: false)
Returns: Number of instructions stepped
```

### `continue_execution`
Resume execution after breakpoint.
```yaml
Returns: "Execution resumed"
```

### `reset`
Reset the emulated machine.
```yaml
Parameters:
  - mode: "soft" or "hard" (default: "soft")
Returns: Reset confirmation
```

</details>

<details>
<summary><b>Breakpoint Management</b> (click to expand)</summary>

### `set_checkpoint`
Set a breakpoint/checkpoint.
```yaml
Parameters:
  - startHex: Start address
  - endHex: End address (optional)
  - stopWhenHit: Stop execution on hit (default: true)
  - enabled: Initially enabled (default: true)
Returns: Checkpoint number and address range
```

### `list_checkpoints`
List all checkpoints.
```yaml
Returns: List with status, address range, hit count
```

### `delete_checkpoint`
Delete a checkpoint.
```yaml
Parameters:
  - checkpointNumber: Checkpoint # to delete
Returns: Confirmation of deletion
```

### `toggle_checkpoint`
Enable or disable a checkpoint.
```yaml
Parameters:
  - checkpointNumber: Checkpoint # to toggle
  - enabled: true to enable, false to disable
Returns: New checkpoint state
```

</details>

<details>
<summary><b>File Operations</b> (click to expand)</summary>

### `load_program`
Load a PRG file into memory.
```yaml
Parameters:
  - filePath: Path to PRG file
  - addressHex: Override load address (optional)
Returns: Load address and size information
```

### `save_memory`
Save memory region to file.
```yaml
Parameters:
  - startHex: Start address
  - endHex: End address
  - filePath: Output file path
  - asPrg: Save as PRG with header (default: true)
Returns: Confirmation with bytes saved
```

</details>

<details>
<summary><b>System Control</b> (click to expand)</summary>

### `start_vice`
Launch a VICE emulator instance.
```yaml
Parameters:
  - emulatorType: x64sc, x128, xvic, xpet, xplus4, xcbm2, xcbm5x0
  - arguments: Additional command line arguments
Returns: Process ID and monitor port
```

### `get_info`
Get VICE version information.
```yaml
Returns: VICE version and SVN revision
```

### `ping`
Check if VICE is responding.
```yaml
Returns: "Pong! VICE is responding"
```

### `get_banks`
List available memory banks.
```yaml
Returns: List of bank numbers and names
```

### `get_display`
Capture current display.
```yaml
Parameters:
  - useVic: Use VIC (true) or VICII/VDC (false)
Returns: Display dimensions and image data size
```

### `quit_vice`
Quit the VICE emulator.
```yaml
Returns: Confirmation of quit
```

### `send_keys`
Send keyboard input to VICE.
```yaml
Parameters:
  - keys: Text to type (use \n for Return)
Returns: Confirmation of keys sent
```

### `execute_batch` ⚡
Execute multiple VICE commands in a single operation for significantly improved performance.
```yaml
Parameters:
  - commandsJson: JSON array of command specifications
  - failFast: Stop on first error (default: true)
Returns: JSON with results of all commands
```
**🚀 Performance Tip**: Always use batch execution for multiple related operations. Setting up a sprite display can be 10x faster using batch vs individual commands. See `batch_examples/` for examples.

</details>

## 💡 Examples

### Debugging a Crash
```
AI: Let me examine what caused the crash...
> read_memory 0x0100 0x01FF  // Check stack
> get_registers              // See CPU state
> read_memory 0xC000 0xC020  // Examine code at PC
```

### Finding Code Patterns
```
AI: I'll search for all JSR instructions to $FFD2...
> search_memory 0x0800 0xBFFF "20 D2 FF"
```

### Interactive Development
```
AI: Let me load your program and set a breakpoint...
> load_program "game.prg"
> set_checkpoint 0x0810      // Break at start
> continue_execution
> step 10 true              // Step over subroutines
```

### Memory Analysis
```
AI: I'll check if the sprite data was copied correctly...
> compare_memory 0x2000 0x3000 64
```

### Batch Operations for Speed ⚡
```
AI: I'll create a red heart sprite in the center of the screen...
> execute_batch [
    {"command": "write_memory", "parameters": {"startHex": "0xD020", "dataHex": "00"}, "description": "Black border"},
    {"command": "write_memory", "parameters": {"startHex": "0xD021", "dataHex": "00"}, "description": "Black background"},
    {"command": "fill_memory", "parameters": {"startHex": "0x0400", "endHex": "0x07E7", "pattern": "20"}, "description": "Clear screen"},
    {"command": "write_memory", "parameters": {"startHex": "0x0340", "dataHex": "00 66 00 01 FF 80 03 FF C0 07 FF E0 0F FF F0..."}, "description": "Heart sprite data"},
    {"command": "write_memory", "parameters": {"startHex": "0x07F8", "dataHex": "0D"}, "description": "Set sprite pointer"},
    {"command": "write_memory", "parameters": {"startHex": "0xD015", "dataHex": "01"}, "description": "Enable sprite 0"},
    {"command": "write_memory", "parameters": {"startHex": "0xD000", "dataHex": "A0"}, "description": "Center X position"},
    {"command": "write_memory", "parameters": {"startHex": "0xD001", "dataHex": "86"}, "description": "Center Y position"},
    {"command": "write_memory", "parameters": {"startHex": "0xD027", "dataHex": "02"}, "description": "Red color"}
  ]
// Result: Beautiful red heart sprite in 1.4 seconds! (vs 14+ seconds individually)
```

## 🏗️ Architecture

ViceMCP is built with:
- **.NET 9.0** - Modern, cross-platform runtime
- **Model Context Protocol** - Standardized AI tool interface
- **vice-bridge-net** - Robust VICE binary monitor implementation
- **Async/await patterns** - Efficient concurrent operations

## 🧪 Development

### Prerequisites
- .NET 9.0 SDK
- VICE emulator
- Git

### Building
```bash
dotnet build
dotnet test
dotnet run --project ViceMCP/ViceMCP.csproj
```

### Testing
```bash
dotnet test
```

Tests use mocking to run without VICE, ensuring fast CI/CD.

## 📖 Documentation

- [Contributing Guidelines](CONTRIBUTING.md)
- [Known Issues](KNOWN_ISSUES.md)

## 🎯 Use Cases

- 🎮 **Game Development** - Debug crashes, optimize routines, trace execution
- 🔍 **Reverse Engineering** - Analyze vintage software behavior
- 📚 **Education** - Learn 6502 assembly with AI assistance
- 🛠️ **Tool Development** - Automate debugging workflows
- 🏆 **Demoscene** - Profile and optimize demo effects

## ⚠️ Known Limitations

See [KNOWN_ISSUES.md](KNOWN_ISSUES.md) for any current limitations.

## 🤝 Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Workflow
1. Fork the repository
2. Create a feature branch
3. Make your changes with tests
4. Submit a pull request

## 📜 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) for details.

## 🙏 Acknowledgments

- [VICE Team](https://vice-emu.sourceforge.io/) - The amazing Commodore emulator
- [Anthropic](https://www.anthropic.com/) - Model Context Protocol
- [Miha Markic](https://github.com/MihaMarkic) - vice-bridge-net library
- The Commodore community for keeping the 8-bit dream alive

---

<div align="center">
Made with ❤️ for the Commodore community
</div>