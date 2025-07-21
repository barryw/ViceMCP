using ViceMCP.ViceBridge.Responses;

namespace ViceMCP.ViceBridge.Commands
{
    /// <summary>
    /// Add text to the keyboard buffer.
    /// </summary>
    public record KeyboardFeedCommand : ViceCommand<EmptyViceResponse>
    {
        /// <summary>
        /// Special characters such as return are escaped with backslashes.
        /// </summary>
        public string Text { get; init; }
        /// <summary>
        /// Creates an instance of <see cref="KeyboardFeedCommand"/>.
        /// </summary>
        /// <param name="text"></param>
        public KeyboardFeedCommand(string text) : base(CommandType.KeyboardFeed)
        {
            try
            {
                Text = EscapeText(text);
                // Debug logging to file in project directory
                try
                {
                    var logPath = "/Users/barry/RiderProjects/ViceMCP/vicemcp_keyboard_debug.log";
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] KeyboardFeed - Original: '{text}', Escaped: '{Text}', Bytes: {string.Join(" ", Text.Select(c => $"{(byte)c:X2}"))}";
                    System.IO.File.AppendAllText(logPath, logEntry + Environment.NewLine);
                }
                catch { }
                
                if (Text.Length > 256)
                {
                    throw new ArgumentException($"Maximum escaped text length is 256 chars: '{Text}'", nameof(text));
                }
            }
            catch (Exception ex)
            {
                // Always log errors
                try
                {
                    var logPath = "/Users/barry/RiderProjects/ViceMCP/vicemcp_keyboard_debug.log";
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] KeyboardFeed EXCEPTION - Original: '{text}', Exception: {ex.GetType().Name}: {ex.Message}";
                    System.IO.File.AppendAllText(logPath, logEntry + Environment.NewLine);
                }
                catch { }
                throw;
            }
        }
        /// <inheritdoc />
        public override uint ContentLength => sizeof(byte) + (uint)Text.Length;
        /// <inheritdoc />
        public override void WriteContent(Span<byte> buffer)
        {
            buffer[0] = (byte)Text.Length;
            // Write PETSCII directly as bytes, not through ASCII encoding
            for (int i = 0; i < Text.Length; i++)
            {
                buffer[i + 1] = (byte)Text[i];
            }
        }

        private static string EscapeText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            var result = new System.Text.StringBuilder(input.Length);

            for (var i = 0; i < input.Length; i++)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    var nextChar = input[i + 1];
                    switch (nextChar)
                    {
                        case 'n':
                            // Send PETSCII return (0x0D)
                            result.Append((char)0x0D);
                            i++; // Skip the next character
                            break;
                        case 'r':
                            // Send PETSCII return (0x0D) - same as \n
                            result.Append((char)0x0D);
                            i++;
                            break;
                        case 't':
                            result.Append((char)0x09); // Tab
                            i++;
                            break;
                        case '\\':
                            result.Append('\\'); // Literal backslash
                            i++;
                            break;
                        case '"':
                            result.Append('"'); // Escaped quote
                            i++;
                            break;
                        case 'b':
                            result.Append((char)0x08); // Backspace
                            i++;
                            break;
                        case 'd':
                            result.Append((char)0x14); // Delete
                            i++;
                            break;
                        case 'u':
                            result.Append((char)0x91); // Cursor up
                            i++;
                            break;
                        case 'l':
                            result.Append((char)0x9D); // Cursor left
                            i++;
                            break;
                        case 'h':
                            result.Append((char)0x13); // Home
                            i++;
                            break;
                        case 'c':
                            result.Append((char)0x93); // Clear screen
                            i++;
                            break;
                        case 's':
                            result.Append((char)0x03); // RUN/STOP
                            i++;
                            break;
                        default:
                            // Unknown escape sequence, keep the backslash
                            result.Append('\\');
                            break;
                    }
                }
                else
                {
                    result.Append(input[i]);
                }
            }

            return result.ToString();
        }
    }
}
