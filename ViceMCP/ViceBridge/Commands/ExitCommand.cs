﻿using ViceMCP.ViceBridge.Responses;

namespace ViceMCP.ViceBridge.Commands
{
    /// <summary>
    /// Exit the monitor until the next breakpoint. 
    /// </summary>
    public record ExitCommand() : ParameterlessCommand<EmptyViceResponse>(CommandType.Exit)
    { }
}
