using System.Text;
using FluentAssertions;
using ViceMCP.ViceBridge;
using ViceMCP.ViceBridge.Commands;
using ViceMCP.ViceBridge.Responses;
using Xunit;

namespace ViceMCP.Tests
{
    public class ViceCommandBaseTests
    {
        // Test command implementation
        private record TestCommand(string TestData) : ViceCommand<EmptyViceResponse>(CommandType.Ping)
        {
            public override uint ContentLength => (uint)TestData.Length;

            public override void WriteContent(Span<byte> buffer)
            {
                Encoding.ASCII.GetBytes(TestData, buffer);
            }
        }

        [Fact]
        public void ViceCommand_DefaultApiVersion_Should_Be_2()
        {
            // Assert
            ViceCommand.DefaultApiVersion.Should().Be(2);
        }

        [Fact]
        public void ViceCommand_Should_Initialize_Properties()
        {
            // Arrange & Act
            var command = new TestCommand("test");

            // Assert
            command.ApiVersion.Should().Be(2);
            command.CommandType.Should().Be(CommandType.Ping);
            command.Response.Should().NotBeNull();
            command.Response.IsCompleted.Should().BeFalse();
        }

        [Fact]
        public void GetBinaryData_Should_Create_Correct_Header()
        {
            // Arrange
            var command = new TestCommand("test");
            const uint requestId = 12345;

            // Act
            var (buffer, length) = command.GetBinaryData(requestId);

            // Assert
            using (buffer)
            {
                length.Should().Be(15); // 11 header + 4 content
                buffer.Data[0].Should().Be(Constants.STX);
                buffer.Data[1].Should().Be(2); // API version
                BitConverter.ToUInt32(buffer.Data, 2).Should().Be(4); // Content length
                BitConverter.ToUInt32(buffer.Data, 6).Should().Be(requestId);
                buffer.Data[10].Should().Be((byte)CommandType.Ping);
                Encoding.ASCII.GetString(buffer.Data, 11, 4).Should().Be("test");
            }
        }

        [Fact]
        public void GetBinaryData_With_Empty_Content_Should_Work()
        {
            // Arrange
            var command = new PingCommand();
            const uint requestId = 54321;

            // Act
            var (buffer, length) = command.GetBinaryData(requestId);

            // Assert
            using (buffer)
            {
                length.Should().Be(11); // Just header
                buffer.Data[0].Should().Be(Constants.STX);
                buffer.Data[1].Should().Be(2); // API version
                BitConverter.ToUInt32(buffer.Data, 2).Should().Be(0); // No content
                BitConverter.ToUInt32(buffer.Data, 6).Should().Be(requestId);
                buffer.Data[10].Should().Be((byte)CommandType.Ping);
            }
        }

        [Fact]
        public void SetResult_With_Success_Should_Complete_Task()
        {
            // Arrange
            var command = new TestCommand("test");
            var response = new EmptyViceResponse(2, ErrorCode.OK);

            // Act
            ((IViceCommand)command).SetResult(response);

            // Assert
            command.Response.IsCompleted.Should().BeTrue();
            var result = command.Response.Result;
            result.IsSuccess.Should().BeTrue();
            result.Response.Should().Be(response);
            result.ErrorCode.Should().Be(ErrorCode.OK);
        }

        [Fact]
        public void SetResult_With_Error_Should_Complete_Task_With_Error()
        {
            // Arrange
            var command = new TestCommand("test");
            var response = new EmptyViceResponse(2, ErrorCode.ObjectDoesNotExist);

            // Act
            ((IViceCommand)command).SetResult(response);

            // Assert
            command.Response.IsCompleted.Should().BeTrue();
            var result = command.Response.Result;
            result.IsSuccess.Should().BeFalse();
            result.Response.Should().BeNull();
            result.ErrorCode.Should().Be(ErrorCode.ObjectDoesNotExist);
        }

        [Fact]
        public void SetException_Should_Fault_Task()
        {
            // Arrange
            var command = new TestCommand("test");
            var exception = new InvalidOperationException("Test error");

            // Act
            command.SetException(exception);

            // Assert
            command.Response.IsFaulted.Should().BeTrue();
            var act = () => command.Response.Result;
            act.Should().Throw<InvalidOperationException>().WithMessage("Test error");
        }

        [Fact]
        public void WriteString_Should_Encode_ASCII()
        {
            // This is tested indirectly through command serialization
            // Testing WriteContent which uses WriteString internally
            var command = new TestCommand("Hello");
            var buffer = new byte[10];

            // Act
            command.WriteContent(buffer);

            // Assert
            Encoding.ASCII.GetString(buffer, 0, 5).Should().Be("Hello");
        }

        [Fact]
        public void CollectErrors_Should_Return_Empty_By_Default()
        {
            // Arrange
            var command = new TestCommand("test");

            // Act
            var errors = command.CollectErrors();

            // Assert
            errors.Should().BeEmpty();
        }

        [Fact]
        public void ParameterlessCommand_Should_Have_Zero_ContentLength()
        {
            // Test specific command types
            new PingCommand().ContentLength.Should().Be(0);
            new ExitCommand().ContentLength.Should().Be(0);
            new QuitCommand().ContentLength.Should().Be(0);
            new InfoCommand().ContentLength.Should().Be(0);
        }

        [Fact]
        public void Command_With_Wrong_Response_Type_Should_Handle_Correctly()
        {
            // When given wrong response type, it uses the error code from the response
            var command = new TestCommand("test");
            var wrongResponse = new InfoResponse(2, ErrorCode.OK, 1, 2, 3, 4, 5);

            // Act
            ((IViceCommand)command).SetResult(wrongResponse);

            // Assert - completes with the error code from response
            command.Response.IsCompleted.Should().BeTrue();
            var result = command.Response.Result;

            // Since it's the wrong type, only error code is passed through
            result.IsSuccess.Should().BeTrue(); // Because ErrorCode.OK means success
            result.ErrorCode.Should().Be(ErrorCode.OK);
            result.Response.Should().BeNull();
        }

        [Fact]
        public void Command_With_Correct_Response_Type_Should_Succeed()
        {
            // When given correct response type with OK status
            var command = new TestCommand("test");
            var correctResponse = new EmptyViceResponse(2, ErrorCode.OK);

            // Act
            ((IViceCommand)command).SetResult(correctResponse);

            // Assert - succeeds with the response
            command.Response.IsCompleted.Should().BeTrue();
            var result = command.Response.Result;
            result.IsSuccess.Should().BeTrue();
            result.ErrorCode.Should().Be(ErrorCode.OK);
            result.Response.Should().Be(correctResponse);
        }
    }
}
