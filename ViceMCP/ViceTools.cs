using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using ViceMCP.ViceBridge;
using ViceMCP.ViceBridge.Commands;
using ViceMCP.ViceBridge.Responses;
using ViceMCP.ViceBridge.Services.Abstract;
using ViceMCP.ViceBridge.Shared;

namespace ViceMCP;

[McpServerToolType]
public class ViceTools
{
    private readonly IViceBridge _viceBridge;
    private readonly ViceConfiguration _config;
    private static bool _isStarted = false;
    private static readonly SemaphoreSlim _startLock = new(1, 1);

    public ViceTools(IViceBridge viceBridge, ViceConfiguration config)
    {
        _viceBridge = viceBridge;
        _config = config;
    }

    [McpServerTool(Name = "read_memory"), Description("Reads memory from VICE.")]
    public async Task<string> ReadMemory(
        [Description("Start address (hex, e.g., 0xc000)")] string startHex,
        [Description("End address (hex, e.g., 0xc0ff)")] string endHex)
    {
        await EnsureStartedAsync();
        
        // Remove 0x prefix if present
        if (startHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            startHex = startHex.Substring(2);
        if (endHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            endHex = endHex.Substring(2);
            
        ushort start = Convert.ToUInt16(startHex, 16);
        ushort end = Convert.ToUInt16(endHex, 16);

        var command = new MemoryGetCommand(0, start, end, MemSpace.MainMemory, 0);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (result.IsSuccess && result.Response != null)
        {
            var memoryResponse = result.Response;
            if (memoryResponse.Memory.HasValue)
            {
                using var buffer = memoryResponse.Memory.Value;
                var bytes = new byte[buffer.Size];
                Array.Copy(buffer.Data, bytes, buffer.Size);
                return BitConverter.ToString(bytes);
            }
            throw new InvalidOperationException("No memory data returned");
        }
        
        throw new InvalidOperationException($"Failed to read memory: {result.ErrorCode}");
    }

    [McpServerTool(Name = "write_memory"), Description("Writes bytes to VICE memory.")]
    public async Task<string> WriteMemory(
        [Description("Start address (hex, e.g., 0xc000)")] string startHex,
        [Description("Byte values (hex, e.g., DE AD BE EF)")] string dataHex)
    {
        await EnsureStartedAsync();
        
        // Remove 0x prefix if present
        if (startHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            startHex = startHex.Substring(2);
            
        ushort start = Convert.ToUInt16(startHex, 16);
        var data = dataHex
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Convert.ToByte(s, 16))
            .ToArray();

        // Create a ManagedBuffer from the data
        using var buffer = ViceMCP.ViceBridge.BufferManager.GetBuffer((uint)data.Length);
        Array.Copy(data, buffer.Data, data.Length);
        
        var command = new MemorySetCommand(0, start, MemSpace.MainMemory, 0, buffer);
        var enqueued = _viceBridge.EnqueueCommand(command, resumeOnStopped: true);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to write memory: {result.ErrorCode}");
        }

        return $"Wrote {data.Length} bytes to ${start:X4}";
    }
    
    private async Task EnsureStartedAsync()
    {
        if (!_isStarted)
        {
            await _startLock.WaitAsync();
            try
            {
                if (!_isStarted)
                {
                    _viceBridge.Start(_config.BinaryMonitorPort);
                    _isStarted = true;
                    
                    // Give it a moment to connect
                    await Task.Delay(500);
                }
            }
            finally
            {
                _startLock.Release();
            }
        }
    }
    
    
    [McpServerTool(Name = "get_registers"), Description("Gets CPU registers from VICE.")]
    public async Task<string> GetRegisters()
    {
        await EnsureStartedAsync();
        
        var command = new RegistersGetCommand(MemSpace.MainMemory);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (result.IsSuccess && result.Response != null)
        {
            var registersResponse = result.Response;
            var registers = new List<string>();
            foreach (var item in registersResponse.Items)
            {
                registers.Add($"Register {item.RegisterId}: ${item.RegisterValue:X4}");
            }
            return string.Join("\n", registers);
        }
        
        throw new InvalidOperationException($"Failed to get registers: {result.ErrorCode}");
    }
    
    [McpServerTool(Name = "set_register"), Description("Sets a CPU register value.")]
    public async Task<string> SetRegister(
        [Description("Register name (e.g., A, X, Y, PC, SP)")] string registerName,
        [Description("Value to set (hex, e.g., 0xFF)")] string valueHex)
    {
        await EnsureStartedAsync();
        
        // Remove 0x prefix if present
        if (valueHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            valueHex = valueHex.Substring(2);
            
        ushort value = Convert.ToUInt16(valueHex, 16);
        
        // Map register name to ID (this would need proper mapping based on VICE docs)
        byte registerId = registerName.ToUpper() switch
        {
            "A" => 0,
            "X" => 1,
            "Y" => 2,
            "PC" => 3,
            "SP" => 4,
            _ => throw new ArgumentException($"Unknown register: {registerName}")
        };
        
        var items = ImmutableArray.Create(new RegisterItem(registerId, value));
        var command = new RegistersSetCommand(MemSpace.MainMemory, items);
        var enqueued = _viceBridge.EnqueueCommand(command, resumeOnStopped: true);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to set register: {result.ErrorCode}");
        }

        return $"Set {registerName} to ${value:X4}";
    }
    
    [McpServerTool(Name = "step"), Description("Steps the CPU by one or more instructions.")]
    public async Task<string> Step(
        [Description("Number of instructions to step (default: 1)")] int count = 1,
        [Description("Step over subroutines (default: false)")] bool stepOver = false)
    {
        await EnsureStartedAsync();
        
        var command = new AdvanceInstructionCommand(stepOver, (ushort)count);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to step: {result.ErrorCode}");
        }

        return $"Stepped {count} instruction(s)";
    }
    
    [McpServerTool(Name = "continue_execution"), Description("Continues execution after a breakpoint.")]
    public async Task<string> ContinueExecution()
    {
        await EnsureStartedAsync();
        
        var command = new ExitCommand();
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to continue execution: {result.ErrorCode}");
        }

        return "Execution resumed";
    }
    
    [McpServerTool(Name = "reset"), Description("Resets the emulated machine.")]
    public async Task<string> Reset(
        [Description("Reset mode: 'soft' or 'hard' (default: 'soft')")] string mode = "soft")
    {
        await EnsureStartedAsync();
        
        var resetMode = mode.ToLower() switch
        {
            "soft" => ResetMode.Soft,
            "hard" => ResetMode.Hard,
            _ => throw new ArgumentException($"Invalid reset mode: {mode}")
        };
        
        var command = new ResetCommand(resetMode);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to reset: {result.ErrorCode}");
        }

        // Resume execution since reset leaves the machine paused
        try
        {
            var continueCmd = new ExitCommand();
            var continueEnqueued = _viceBridge.EnqueueCommand(continueCmd);
            var continueResult = await continueEnqueued.Response;
            if (continueResult.ErrorCode != ErrorCode.OK)
            {
                Console.Error.WriteLine($"Warning: Failed to resume execution after reset: {continueResult.ErrorCode}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not auto-resume execution after reset: {ex.Message}");
        }

        return $"Machine reset ({mode})";
    }
    
    [McpServerTool(Name = "get_info"), Description("Gets VICE emulator info.")]
    public async Task<string> GetInfo()
    {
        await EnsureStartedAsync();
        
        var command = new InfoCommand();
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (result.IsSuccess && result.Response != null)
        {
            var info = result.Response;
            return $"VICE Version: {info.Major}.{info.Minor}.{info.Build}.{info.Revision}\nSVN: {info.SvnVersion}";
        }
        
        throw new InvalidOperationException($"Failed to get info: {result.ErrorCode}");
    }
    
    [McpServerTool(Name = "ping"), Description("Pings the VICE emulator.")]
    public async Task<string> Ping()
    {
        await EnsureStartedAsync();
        
        var command = new PingCommand();
        var enqueued = _viceBridge.EnqueueCommand(command);
        
        // Create a 2 second timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        
        try
        {
            var result = await enqueued.Response.WaitAsync(cts.Token);
            
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to ping: {result.ErrorCode}");
            }
            return "Pong! VICE is responding";
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("VICE did not respond to ping within 2 seconds - it may not be running");
        }
    }
    
    [McpServerTool(Name = "get_banks"), Description("Gets available memory banks.")]
    public async Task<string> GetBanks()
    {
        await EnsureStartedAsync();
        
        var command = new BanksAvailableCommand();
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (result.IsSuccess && result.Response != null)
        {
            var banksResponse = result.Response;
            var banks = new List<string>();
            foreach (var bank in banksResponse.Banks)
            {
                banks.Add($"Bank {bank.BankId}: {bank.Name}");
            }
            return string.Join("\n", banks);
        }
        
        throw new InvalidOperationException($"Failed to get banks: {result.ErrorCode}");
    }
    
    [McpServerTool(Name = "set_checkpoint"), Description("Sets a breakpoint/checkpoint.")]
    public async Task<string> SetCheckpoint(
        [Description("Start address (hex)")] string startHex,
        [Description("End address (hex, optional - same as start if not provided)")] string? endHex = null,
        [Description("Stop when hit (default: true)")] bool stopWhenHit = true,
        [Description("Enabled (default: true)")] bool enabled = true)
    {
        await EnsureStartedAsync();
        
        // Remove 0x prefix if present
        if (startHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            startHex = startHex.Substring(2);
        if (endHex != null && endHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            endHex = endHex.Substring(2);
            
        ushort start = Convert.ToUInt16(startHex, 16);
        ushort end = endHex != null ? Convert.ToUInt16(endHex, 16) : start;
        
        var command = new CheckpointSetCommand(start, end, stopWhenHit, enabled, CpuOperation.Exec, false);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (result.IsSuccess && result.Response != null)
        {
            var checkpointResponse = result.Response;
            return $"Checkpoint {checkpointResponse.CheckpointNumber} set at ${start:X4}-${end:X4}";
        }
        
        throw new InvalidOperationException($"Failed to set checkpoint: {result.ErrorCode}");
    }
    
    [McpServerTool(Name = "list_checkpoints"), Description("Lists all checkpoints.")]
    public async Task<string> ListCheckpoints()
    {
        await EnsureStartedAsync();
        
        var command = new CheckpointListCommand();
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (result.IsSuccess && result.Response != null)
        {
            var checkpointsResponse = result.Response;
            if (checkpointsResponse.Info.Length == 0)
            {
                return "No checkpoints set";
            }
            
            var checkpoints = new List<string>();
            foreach (var cp in checkpointsResponse.Info)
            {
                var status = cp.Enabled ? "enabled" : "disabled";
                var hit = cp.CurrentlyHit ? " [HIT]" : "";
                checkpoints.Add($"#{cp.CheckpointNumber}: ${cp.StartAddress:X4}-${cp.EndAddress:X4} ({status}) Hits: {cp.HitCount}{hit}");
            }
            return string.Join("\n", checkpoints);
        }
        
        throw new InvalidOperationException($"Failed to list checkpoints: {result.ErrorCode}");
    }
    
    [McpServerTool(Name = "delete_checkpoint"), Description("Deletes a checkpoint.")]
    public async Task<string> DeleteCheckpoint(
        [Description("Checkpoint number to delete")] uint checkpointNumber)
    {
        await EnsureStartedAsync();
        
        var command = new CheckpointDeleteCommand(checkpointNumber);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to delete checkpoint: {result.ErrorCode}");
        }

        return $"Deleted checkpoint #{checkpointNumber}";
    }
    
    [McpServerTool(Name = "toggle_checkpoint"), Description("Enables or disables a checkpoint.")]
    public async Task<string> ToggleCheckpoint(
        [Description("Checkpoint number")] uint checkpointNumber,
        [Description("Enable (true) or disable (false)")] bool enabled)
    {
        await EnsureStartedAsync();
        
        var command = new CheckpointToggleCommand(checkpointNumber, enabled);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to toggle checkpoint: {result.ErrorCode}");
        }

        return $"Checkpoint #{checkpointNumber} {(enabled ? "enabled" : "disabled")}";
    }
    
    [McpServerTool(Name = "get_display"), Description("Gets the current display/screen as an image.")]
    public async Task<string> GetDisplay(
        [Description("Use VIC display (true) or VICII/VDC (false) - default: true")] bool useVic = true)
    {
        await EnsureStartedAsync();
        
        var command = new DisplayGetCommand(useVic, ImageFormat.Indexed);
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (result.IsSuccess && result.Response != null)
        {
            var displayResponse = result.Response;
            if (displayResponse.Image != null)
            {
                using var image = displayResponse.Image.Value;
                return $"Display captured: {displayResponse.InnerWidth}x{displayResponse.InnerHeight} ({displayResponse.BitsPerPixel} bpp)\nImage data: {image.Size} bytes";
            }
            throw new InvalidOperationException("No image data returned");
        }
        
        throw new InvalidOperationException($"Failed to get display: {result.ErrorCode}");
    }
    
    [McpServerTool(Name = "quit_vice"), Description("Quits the VICE emulator.")]
    public async Task<string> QuitVice()
    {
        await EnsureStartedAsync();
        
        var command = new QuitCommand();
        var enqueued = _viceBridge.EnqueueCommand(command);
        var result = await enqueued.Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to quit VICE: {result.ErrorCode}");
        }

        _isStarted = false;
        return "VICE emulator quit";
    }
    
    [McpServerTool(Name = "start_vice"), Description("Starts a VICE emulator instance.")]
    public async Task<string> StartVice(
        [Description("Emulator type: x64sc (C64), x128 (C128), xvic (VIC20), xpet (PET), etc.")] string emulatorType = "x64sc",
        [Description("Additional command line arguments")] string? arguments = null)
    {
        var validEmulators = new[] { "x64sc", "x64", "x128", "xvic", "xpet", "xplus4", "xcbm2", "xcbm5x0" };
        if (!validEmulators.Contains(emulatorType.ToLower()))
        {
            throw new ArgumentException($"Invalid emulator type. Valid types: {string.Join(", ", validEmulators)}");
        }
        
        // Get emulator path from configuration
        var emulatorPath = _config.GetEmulatorPath(emulatorType);
        
        // Check if emulator exists at the resolved path
        if (!File.Exists(emulatorPath) && !Path.IsPathRooted(emulatorPath))
        {
            // If it's not a full path and doesn't exist, let the system try to find it in PATH
            // This is handled by ProcessStartInfo
        }
        else if (!File.Exists(emulatorPath))
        {
            throw new FileNotFoundException($"VICE emulator not found at: {emulatorPath}. " +
                $"Set VICE_BIN_PATH environment variable to the directory containing VICE binaries.");
        }
        
        // Start VICE with binary monitor enabled
        var processArgs = $"-binarymonitor -binarymonitoraddress 127.0.0.1:{_config.BinaryMonitorPort}";
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            processArgs += " " + arguments;
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = emulatorPath,
            Arguments = processArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        
        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start {emulatorType}");
        }
        
        // Give VICE time to start and open the binary monitor port
        await Task.Delay(_config.StartupTimeout);
        
        // Resume execution since VICE starts paused when binary monitor is enabled
        try
        {
            await EnsureStartedAsync();
            var continueCmd = new ExitCommand();
            var continueEnqueued = _viceBridge.EnqueueCommand(continueCmd);
            var result = await continueEnqueued.Response;
            if (result.ErrorCode != ErrorCode.OK)
            {
                Console.Error.WriteLine($"Warning: Failed to resume execution: {result.ErrorCode}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not auto-resume execution: {ex.Message}");
        }
        
        return $"Started {emulatorType} (PID: {process.Id}) with binary monitor on port {_config.BinaryMonitorPort}";
    }
    
    [McpServerTool(Name = "copy_memory"), Description("Copies memory from source to destination.")]
    public async Task<string> CopyMemory(
        [Description("Source start address (hex)")] string sourceHex,
        [Description("Destination start address (hex)")] string destHex,
        [Description("Number of bytes to copy")] int length)
    {
        await EnsureStartedAsync();
        
        if (length <= 0 || length > 65536)
        {
            throw new ArgumentException("Length must be between 1 and 65536");
        }
        
        // Remove 0x prefix if present
        if (sourceHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            sourceHex = sourceHex.Substring(2);
        if (destHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            destHex = destHex.Substring(2);
            
        ushort source = Convert.ToUInt16(sourceHex, 16);
        ushort dest = Convert.ToUInt16(destHex, 16);
        ushort endSource = (ushort)(source + length - 1);
        
        // Read from source
        var readCommand = new MemoryGetCommand(0, source, endSource, MemSpace.MainMemory, 0);
        var readResult = await _viceBridge.EnqueueCommand(readCommand).Response;
        
        if (!readResult.IsSuccess || readResult.Response?.Memory == null)
        {
            throw new InvalidOperationException($"Failed to read source memory: {readResult.ErrorCode}");
        }
        
        using var sourceBuffer = readResult.Response.Memory.Value;
        
        // Write to destination
        var destBuffer = BufferManager.GetBuffer((uint)length);
        Array.Copy(sourceBuffer.Data, 0, destBuffer.Data, 0, length);
        
        var writeCommand = new MemorySetCommand(0, dest, MemSpace.MainMemory, 0, destBuffer);
        var writeResult = await _viceBridge.EnqueueCommand(writeCommand, resumeOnStopped: true).Response;
        
        if (!writeResult.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to write to destination: {writeResult.ErrorCode}");
        }
        
        return $"Copied {length} bytes from ${source:X4} to ${dest:X4}";
    }
    
    [McpServerTool(Name = "fill_memory"), Description("Fills memory region with a byte pattern.")]
    public async Task<string> FillMemory(
        [Description("Start address (hex)")] string startHex,
        [Description("End address (hex)")] string endHex,
        [Description("Fill pattern (hex bytes, e.g., 'FF' or 'AA 55')")] string pattern)
    {
        await EnsureStartedAsync();
        
        // Remove 0x prefix if present
        if (startHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            startHex = startHex.Substring(2);
        if (endHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            endHex = endHex.Substring(2);
            
        ushort start = Convert.ToUInt16(startHex, 16);
        ushort end = Convert.ToUInt16(endHex, 16);
        
        if (end < start)
        {
            throw new ArgumentException("End address must be greater than or equal to start address");
        }
        
        // Parse pattern
        var patternBytes = pattern.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Convert.ToByte(s, 16))
            .ToArray();
            
        if (patternBytes.Length == 0)
        {
            throw new ArgumentException("Pattern must contain at least one byte");
        }
        
        int length = end - start + 1;
        var buffer = BufferManager.GetBuffer((uint)length);
        
        // Fill buffer with pattern
        for (int i = 0; i < length; i++)
        {
            buffer.Data[i] = patternBytes[i % patternBytes.Length];
        }
        
        var command = new MemorySetCommand(0, start, MemSpace.MainMemory, 0, buffer);
        var result = await _viceBridge.EnqueueCommand(command, resumeOnStopped: true).Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to fill memory: {result.ErrorCode}");
        }
        
        return $"Filled ${start:X4}-${end:X4} with pattern {string.Join(" ", patternBytes.Select(b => $"{b:X2}"))}";
    }
    
    [McpServerTool(Name = "search_memory"), Description("Searches for a byte pattern in memory.")]
    public async Task<string> SearchMemory(
        [Description("Start address (hex)")] string startHex,
        [Description("End address (hex)")] string endHex,
        [Description("Search pattern (hex bytes, e.g., 'A9 00' for LDA #$00)")] string pattern,
        [Description("Maximum results to return (default: 10)")] int maxResults = 10)
    {
        await EnsureStartedAsync();
        
        // Remove 0x prefix if present
        if (startHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            startHex = startHex.Substring(2);
        if (endHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            endHex = endHex.Substring(2);
            
        ushort start = Convert.ToUInt16(startHex, 16);
        ushort end = Convert.ToUInt16(endHex, 16);
        
        // Parse search pattern
        var searchBytes = pattern.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Convert.ToByte(s, 16))
            .ToArray();
            
        if (searchBytes.Length == 0)
        {
            throw new ArgumentException("Search pattern must contain at least one byte");
        }
        
        // Read memory in chunks
        var matches = new List<ushort>();
        const int chunkSize = 4096;
        
        for (ushort addr = start; addr <= end && matches.Count < maxResults; )
        {
            ushort chunkEnd = (ushort)Math.Min(addr + chunkSize - 1, end);
            
            var command = new MemoryGetCommand(0, addr, chunkEnd, MemSpace.MainMemory, 0);
            var result = await _viceBridge.EnqueueCommand(command).Response;
            
            if (result.IsSuccess && result.Response?.Memory != null)
            {
                using var buffer = result.Response.Memory.Value;
                var data = new byte[buffer.Size];
                Array.Copy(buffer.Data, data, buffer.Size);
                
                // Search for pattern in chunk
                for (int i = 0; i <= data.Length - searchBytes.Length && matches.Count < maxResults; i++)
                {
                    bool found = true;
                    for (int j = 0; j < searchBytes.Length; j++)
                    {
                        if (data[i + j] != searchBytes[j])
                        {
                            found = false;
                            break;
                        }
                    }
                    
                    if (found)
                    {
                        matches.Add((ushort)(addr + i));
                    }
                }
            }
            
            addr = (ushort)(chunkEnd + 1);
        }
        
        if (matches.Count == 0)
        {
            return $"Pattern not found in ${start:X4}-${end:X4}";
        }
        
        var matchList = string.Join("\n", matches.Select(m => $"  ${m:X4}"));
        return $"Found {matches.Count} match(es) for pattern {pattern}:\n{matchList}";
    }
    
    [McpServerTool(Name = "send_keys"), Description("Sends keyboard input to VICE.")]
    public async Task<string> SendKeys(
        [Description("Text to type (special keys use backslash escape, e.g., 'HELLO\\n' for HELLO + Return)")] string keys)
    {
        await EnsureStartedAsync();
        
        // The KeyboardFeedCommand handles escape sequences internally
        // Supported escape sequences:
        // \n or \r = Return (PETSCII 0x0D)
        // \t = Tab (0x09)
        // \\ = Literal backslash
        // \" = Literal quote
        // \b = Backspace (0x08)
        // \d = Delete (0x14)
        // \u = Cursor up (0x91)
        // \l = Cursor left (0x9D)
        // \h = Home (0x13)
        // \c = Clear screen (0x93)
        // \s = RUN/STOP (0x03)
        
        try
        {
            var command = new KeyboardFeedCommand(keys);
            
            // Log before sending
            try
            {
                var logPath = "/Users/barry/RiderProjects/ViceMCP/vicemcp_keyboard_debug.log";
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] About to send command to ViceBridge...";
                await File.AppendAllTextAsync(logPath, logEntry + Environment.NewLine);
            }
            catch { }
            
            var result = await _viceBridge.EnqueueCommand(command).Response;
            
            if (!result.IsSuccess)
            {
                // Log to file for debugging
                try
                {
                    var logPath = "/Users/barry/RiderProjects/ViceMCP/vicemcp_keyboard_debug.log";
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] send_keys failed: ErrorCode={result.ErrorCode} ({(int)result.ErrorCode}), Keys='{keys}'";
                    await File.AppendAllTextAsync(logPath, logEntry + Environment.NewLine);
                }
                catch { }
                throw new InvalidOperationException($"Failed to send keys: {result.ErrorCode}");
            }
            
            return $"Sent '{keys}' to keyboard buffer";
        }
        catch (ArgumentException ex)
        {
            // Log to file for debugging
            try
            {
                var logPath = "/Users/barry/RiderProjects/ViceMCP/vicemcp_keyboard_debug.log";
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] send_keys ArgumentException: {ex.Message}, Keys='{keys}'";
                await File.AppendAllTextAsync(logPath, logEntry + Environment.NewLine);
            }
            catch { }
            throw;
        }
    }
    
    [McpServerTool(Name = "compare_memory"), Description("Compares two memory regions.")]
    public async Task<string> CompareMemory(
        [Description("First region start address (hex)")] string addr1Hex,
        [Description("Second region start address (hex)")] string addr2Hex,
        [Description("Number of bytes to compare")] int length)
    {
        await EnsureStartedAsync();
        
        if (length <= 0 || length > 65536)
        {
            throw new ArgumentException("Length must be between 1 and 65536");
        }
        
        // Remove 0x prefix if present
        if (addr1Hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            addr1Hex = addr1Hex.Substring(2);
        if (addr2Hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            addr2Hex = addr2Hex.Substring(2);
            
        ushort addr1 = Convert.ToUInt16(addr1Hex, 16);
        ushort addr2 = Convert.ToUInt16(addr2Hex, 16);
        ushort end1 = (ushort)(addr1 + length - 1);
        ushort end2 = (ushort)(addr2 + length - 1);
        
        // Read both regions
        var cmd1 = new MemoryGetCommand(0, addr1, end1, MemSpace.MainMemory, 0);
        var cmd2 = new MemoryGetCommand(0, addr2, end2, MemSpace.MainMemory, 0);
        
        var result1 = await _viceBridge.EnqueueCommand(cmd1).Response;
        var result2 = await _viceBridge.EnqueueCommand(cmd2).Response;
        
        if (!result1.IsSuccess || result1.Response?.Memory == null)
        {
            throw new InvalidOperationException($"Failed to read first region: {result1.ErrorCode}");
        }
        if (!result2.IsSuccess || result2.Response?.Memory == null)
        {
            throw new InvalidOperationException($"Failed to read second region: {result2.ErrorCode}");
        }
        
        using var buffer1 = result1.Response.Memory.Value;
        using var buffer2 = result2.Response.Memory.Value;
        
        var differences = new List<string>();
        int diffCount = 0;
        
        for (int i = 0; i < length && diffCount < 10; i++)
        {
            if (buffer1.Data[i] != buffer2.Data[i])
            {
                differences.Add($"  ${addr1 + i:X4}: ${buffer1.Data[i]:X2} != ${addr2 + i:X4}: ${buffer2.Data[i]:X2}");
                diffCount++;
            }
        }
        
        if (diffCount == 0)
        {
            return $"Memory regions ${addr1:X4}-${end1:X4} and ${addr2:X4}-${end2:X4} are identical";
        }
        
        var result = $"Found {diffCount} difference(s) in first {Math.Min(diffCount, 10)} bytes:\n";
        result += string.Join("\n", differences);
        
        if (diffCount == 10)
        {
            result += "\n... (more differences exist)";
        }
        
        return result;
    }
    
    [McpServerTool(Name = "load_program"), Description("Loads a PRG file into memory.")]
    public async Task<string> LoadProgram(
        [Description("Path to PRG file")] string filePath,
        [Description("Override load address (hex, optional - uses PRG header if not specified)")] string? addressHex = null)
    {
        await EnsureStartedAsync();
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"PRG file not found: {filePath}");
        }
        
        var prgData = await File.ReadAllBytesAsync(filePath);
        
        if (prgData.Length < 2)
        {
            throw new InvalidOperationException("PRG file too small - must contain at least load address");
        }
        
        // Get load address from PRG header or override
        ushort loadAddress;
        int dataOffset;
        
        if (addressHex != null)
        {
            if (addressHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addressHex = addressHex.Substring(2);
            loadAddress = Convert.ToUInt16(addressHex, 16);
            dataOffset = 0; // Use entire file
        }
        else
        {
            // Read load address from first two bytes (little-endian)
            loadAddress = (ushort)(prgData[0] | (prgData[1] << 8));
            dataOffset = 2; // Skip address bytes
        }
        
        int dataLength = prgData.Length - dataOffset;
        
        if (dataLength <= 0)
        {
            throw new InvalidOperationException("No data to load after address bytes");
        }
        
        // Create buffer with program data
        var buffer = BufferManager.GetBuffer((uint)dataLength);
        Array.Copy(prgData, dataOffset, buffer.Data, 0, dataLength);
        
        // Write to memory
        var command = new MemorySetCommand(0, loadAddress, MemSpace.MainMemory, 0, buffer);
        var result = await _viceBridge.EnqueueCommand(command, resumeOnStopped: true).Response;
        
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to load program: {result.ErrorCode}");
        }
        
        var endAddress = loadAddress + dataLength - 1;
        return $"Loaded {Path.GetFileName(filePath)} ({dataLength} bytes) to ${loadAddress:X4}-${endAddress:X4}";
    }
    
    [McpServerTool(Name = "save_memory"), Description("Saves a memory region to file.")]
    public async Task<string> SaveMemory(
        [Description("Start address (hex)")] string startHex,
        [Description("End address (hex)")] string endHex,
        [Description("Output file path")] string filePath,
        [Description("Save as PRG file with load address header (default: true)")] bool asPrg = true)
    {
        await EnsureStartedAsync();
        
        // Remove 0x prefix if present
        if (startHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            startHex = startHex.Substring(2);
        if (endHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            endHex = endHex.Substring(2);
            
        ushort start = Convert.ToUInt16(startHex, 16);
        ushort end = Convert.ToUInt16(endHex, 16);
        
        if (end < start)
        {
            throw new ArgumentException("End address must be greater than or equal to start address");
        }
        
        // Read memory
        var command = new MemoryGetCommand(0, start, end, MemSpace.MainMemory, 0);
        var result = await _viceBridge.EnqueueCommand(command).Response;
        
        if (!result.IsSuccess || result.Response?.Memory == null)
        {
            throw new InvalidOperationException($"Failed to read memory: {result.ErrorCode}");
        }
        
        using var buffer = result.Response.Memory.Value;
        
        // Prepare data to save
        byte[] dataToSave;
        if (asPrg)
        {
            // Add PRG header with load address
            dataToSave = new byte[buffer.Size + 2];
            dataToSave[0] = (byte)(start & 0xFF);
            dataToSave[1] = (byte)(start >> 8);
            Array.Copy(buffer.Data, 0, dataToSave, 2, buffer.Size);
        }
        else
        {
            // Raw binary
            dataToSave = new byte[buffer.Size];
            Array.Copy(buffer.Data, 0, dataToSave, 0, buffer.Size);
        }
        
        // Save to file
        await File.WriteAllBytesAsync(filePath, dataToSave);
        
        var fileType = asPrg ? "PRG" : "binary";
        return $"Saved ${start:X4}-${end:X4} ({buffer.Size} bytes) to {filePath} as {fileType} file";
    }
    
    [McpServerTool(Name = "execute_batch"), Description("Executes multiple VICE commands in a single batch operation. IMPORTANT: Always use this for multiple related operations (e.g., setting up screens, sprites, memory initialization) as it's significantly faster than individual commands - often 10x performance improvement. See batch_examples/ for JSON format.")]
    public async Task<string> ExecuteBatch(
        [Description("JSON array of command specifications")] string commandsJson,
        [Description("Stop execution on first error (default: true)")] bool failFast = true)
    {
        await EnsureStartedAsync();
        
        List<BatchCommandSpec> commands;
        try
        {
            commands = JsonSerializer.Deserialize<List<BatchCommandSpec>>(commandsJson) ?? new List<BatchCommandSpec>();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON format: {ex.Message}");
        }
        
        if (commands.Count == 0)
        {
            throw new ArgumentException("No commands provided");
        }
        
        var builder = new BatchCommandBuilder(this);
        var response = await builder.ExecuteBatchAsync(commands, failFast);
        
        return JsonSerializer.Serialize(response, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }
    
    [McpServerTool(Name = "hit_run_stop"), Description("Simulates pressing RUN/STOP to break a running BASIC program")]
    public async Task<string> HitRunStop()
    {
        await EnsureStartedAsync();
        
        try
        {
            // Step 1: Step one instruction to pause execution
            var stepCmd = new AdvanceInstructionCommand(false, 1);
            var stepResult = await _viceBridge.EnqueueCommand(stepCmd).Response;
            if (!stepResult.IsSuccess)
            {
                throw new InvalidOperationException("Failed to step (pause execution)");
            }
            
            // Step 2: Get current line number before breaking (for reporting)
            var lineNumCmd = new MemoryGetCommand(0, 0x39, 0x3A, MemSpace.MainMemory, 0);
            var lineResult = await _viceBridge.EnqueueCommand(lineNumCmd).Response;
            
            int currentLine = -1;
            if (lineResult.IsSuccess && lineResult.Response?.Memory != null)
            {
                using var buffer = lineResult.Response.Memory.Value;
                // Line number is stored as little-endian 16-bit value
                currentLine = buffer.Data[0] | (buffer.Data[1] << 8);
            }
            
            // Step 3: Set both X register and PC in a single RegistersSetCommand for atomicity
            var registers = ImmutableArray.Create(
                new RegisterItem(1, 30),      // X register = 30 (decimal)
                new RegisterItem(3, 0xA437)   // PC = $A437
            );
            var setRegsCmd = new RegistersSetCommand(MemSpace.MainMemory, registers);
            var setRegsResult = await _viceBridge.EnqueueCommand(setRegsCmd).Response;
            if (!setRegsResult.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to set registers: {setRegsResult.ErrorCode}");
            }
            
            
            // Step 5: Continue execution to process the break
            var continueCmd = new ExitCommand();
            var continueResult = await _viceBridge.EnqueueCommand(continueCmd).Response;
            if (!continueResult.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to continue execution: {continueResult.ErrorCode}");
            }
            
            // Step 6: Wait a bit for BASIC to process the break
            await Task.Delay(100);
            
            // Step 7: Verify we're back at READY prompt
            var verifyCmd = new RegistersGetCommand(MemSpace.MainMemory);
            var verifyResult = await _viceBridge.EnqueueCommand(verifyCmd).Response;
            
            if (verifyResult.IsSuccess && verifyResult.Response != null)
            {
                // Find PC in the registers response
                var pcReg = verifyResult.Response.Items.FirstOrDefault(r => r.RegisterId == 3);
                if (pcReg != null)
                {
                    var pc = pcReg.RegisterValue;
                    // Check if PC is in BASIC idle loop range
                    if (pc >= 0xE5CD && pc <= 0xE5D4)
                    {
                        if (currentLine >= 0 && currentLine < 65535)
                        {
                            return $"BREAK in line {currentLine}";
                        }
                        else
                        {
                            return "BREAK (program stopped)";
                        }
                    }
                    else
                    {
                        return $"Break attempted - PC at ${pc:X4} (may need to retry)";
                    }
                }
            }
            
            return "Break command sent";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to simulate RUN/STOP: {ex.Message}", ex);
        }
    }
}