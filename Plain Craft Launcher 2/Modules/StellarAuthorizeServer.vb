Imports System.Net
Imports System.Net.Sockets
Imports System.Web
Imports PCL.Core.App

Public Class StellarAuthorizeServer
    Private _listener As HttpListener
    Private _cts As CancellationTokenSource

    Private Shared Function GetFreePort() As Integer
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Dim p = CType(l.LocalEndpoint, IPEndPoint).Port
        l.Stop()
        Return p
    End Function

    Public Async Function StartAuthorizeAsync() As Task(Of String)
        _cts = New CancellationTokenSource()
        Dim tcs As New TaskCompletionSource(Of String)()
        Dim state = Guid.NewGuid().ToString("N")

        Dim url = $"http://console.stellarfrp.top/Authorize?client_id=stellarfrp-mc&redirect_uri=%r&state={state}"
        Dim started = ModWebServer.StartOAuthWaitingCallback("StellarFrp", url,
            Function(success As Boolean, parameters As IDictionary(Of String, String), content As String)
                If Not success Then
                    tcs.TrySetResult(Nothing)
                    Return ModWebServer.OAuthCompleteStatus.Failed(content)
                End If
                Try
                    Dim qState = If(parameters.ContainsKey("state"), parameters("state"), Nothing)
                    Dim qType = If(parameters.ContainsKey("type"), parameters("type"), Nothing)
                    Dim qToken = If(parameters.ContainsKey("token"), parameters("token"), Nothing)
                    If (qState Is Nothing OrElse qState = state) AndAlso qType = "allowed" AndAlso Not String.IsNullOrWhiteSpace(qToken) Then
                        Config.Link.StellarToken = qToken
                        tcs.TrySetResult(qToken)
                        Return ModWebServer.OAuthCompleteStatus.Complete("ok")
                    Else
                        tcs.TrySetResult(Nothing)
                        Return ModWebServer.OAuthCompleteStatus.Failed("授权失败或回调参数无效")
                    End If
                Catch ex As Exception
                    tcs.TrySetResult(Nothing)
                    Return ModWebServer.OAuthCompleteStatus.Failed("处理授权回调失败", ex)
                End Try
            End Function, redirectTemplate:=$"http://127.0.0.1:{"{port}"}/static/?callback")

        If Not started Then
            tcs.TrySetResult(Nothing)
        End If

        Dim token = Await tcs.Task.ConfigureAwait(False)
        Return token
    End Function

    Public Sub Cancel()
        Try
            _cts?.Cancel()
            _listener?.Close()
        Catch
        End Try
    End Sub
End Class