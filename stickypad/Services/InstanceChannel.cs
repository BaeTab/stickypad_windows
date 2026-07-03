using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StickyPad.Services;

/// 단일 인스턴스 앱에서 "이미 실행 중인 인스턴스에게 파일 경로를 넘겨주는" 채널.
/// 바탕화면에서 .md 를 더블클릭(또는 '연결 프로그램'으로 열기)하면 두 번째 프로세스가 뜨는데,
/// 그 프로세스가 경로를 named pipe 로 첫 인스턴스에 보내고 조용히 종료한다.
public sealed class InstanceChannel : IDisposable
{
    private const string PipeName = "StickyPad.OpenFile.v1";

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// 실행 중인 인스턴스가 호출 — 들어오는 경로마다 onPaths(줄단위 여러 개 가능)를 실행.
    public void StartServer(Action<string[]> onPaths)
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => ServerLoopAsync(onPaths, _cts.Token));
    }

    private static async Task ServerLoopAsync(Action<string[]> onPaths, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync(token).ConfigureAwait(false);

                var paths = payload
                    .Replace("\r\n", "\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (paths.Length > 0) onPaths(paths);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 개별 연결 실패는 무시하고 다음 연결을 계속 받는다.
            }
        }
    }

    /// 새로 뜬(두 번째) 프로세스가 호출 — 실행 중인 인스턴스에 경로들을 전달. 성공하면 true.
    public static bool TrySend(string[] paths, int timeoutMs = 1500)
    {
        if (paths.Length == 0) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(string.Join("\n", paths));
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _loop?.Wait(500);
        }
        catch { /* nothing to do */ }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
