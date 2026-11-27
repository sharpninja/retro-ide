using System.Text;
using Avalonia.RemoteControl.Protocol.V1;
using Avalonia.RemoteControl.Server.Input;
using RetroIde.IntegrationTests;
using RetroIde.Testing;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlGitTests
{
    // Use disposable fixture credentials so input follows the same route as the user's native Git prompts.
    private static async Task VerifyInteractiveGitPromptAsync(RemoteControlInputDispatcher input, GitWorktreeFixture sample,
        StringBuilder rendered, CancellationToken token)
    {
        const string reference = "refs/heads/main";
        var expected = (await sample.GitAsync(sample.Remote, token, "rev-parse", reference).ConfigureAwait(true)).Trim();
        var password = Guid.NewGuid().ToString("N");
        var fixture = new AuthenticatedGitRemote(sample.Remote, password);
        var server = await fixture.StartAsync(token).ConfigureAwait(true);
        var offset = rendered.Length;
        await using (server.ConfigureAwait(true))
        {
            var endpoint = AuthenticatedGitRemote.RemoteUrl(server);
            await SendTerminalLineAsync(input,
                $"git -c credential.helper= -c credential.interactive=true -c core.askPass= -c http.proxy= ls-remote {endpoint} {reference}",
                token).ConfigureAwait(true);
            await WaitForOutputAsync("Username for ").ConfigureAwait(true);
            await SendTerminalLineAsync(input, "fixture", token).ConfigureAwait(true);
            await WaitForOutputAsync("Password for ").ConfigureAwait(true);
            await SendTerminalLineAsync(input, password, token).ConfigureAwait(true);
            await WaitForOutputAsync(expected + "\t" + reference).ConfigureAwait(true);
            if (rendered.ToString(offset, rendered.Length - offset).Contains(password, StringComparison.Ordinal))
                throw new InvalidOperationException("The terminal echoed the disposable Git fixture password.");
        }

        Console.WriteLine("RemoteControl answered native Git username and password prompts and received the exact remote ref.");

        async Task WaitForOutputAsync(string marker)
        {
            while (!rendered.ToString(offset, rendered.Length - offset).Contains(marker, StringComparison.Ordinal))
                await Task.Delay(20, token).ConfigureAwait(true);
        }
    }

    private static async Task SendTerminalLineAsync(RemoteControlInputDispatcher input, string text, CancellationToken token)
    {
        var sent = await input.SendInputAsync(
            [new RemoteInputEvent { Kind = RemoteInputKind.Text, Text = text },
                new RemoteInputEvent { Kind = RemoteInputKind.KeyDown, Key = "Enter" },
                new RemoteInputEvent { Kind = RemoteInputKind.KeyUp, Key = "Enter" }],
            "retro-ide-tests").AsTask().WaitAsync(token).ConfigureAwait(true);
        if (!sent.Succeeded)
            throw new InvalidOperationException(sent.Message);
    }
}
