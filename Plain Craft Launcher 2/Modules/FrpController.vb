Imports System.Text
Imports System.Diagnostics
Imports PCL.Core.App
Imports PCL.Core.Utils.OS
Imports PCL.Core.IO

Public Module FrpController
    Private _proc As Process = Nothing
    Public PublicHost As String = ""
    Public PublicPort As Integer = 0
    Public IsRunning As Boolean = False

    Private Function GetFrpcVersion() As String
        Dim v = Setup.Get("FrpcVersion")
        If String.IsNullOrWhiteSpace(v) Then v = "0.61.2"
        Return v
    End Function

    Private Function GetFrpcPath() As String
        Return IO.Path.Combine(FileService.LocalDataPath, "frp", GetFrpcVersion(), "StellarFrpc.exe")
    End Function

    Public Function Precheck() As Integer
        Try
            If Not IO.File.Exists(GetFrpcPath()) Then Return 1
            Return 0
        Catch
            Return 1
        End Try
    End Function

    Public Function EnsureDependencies() As Boolean
        Return IO.File.Exists(GetFrpcPath())
    End Function

    Public Async Function StartAsync(worldPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Boolean)
        Await Task.Yield()
        Try
            Log("[FRP] StartAsync")
            Log("[FRP] Executable: " & GetFrpcPath())
            Dim host = Setup.Get("FrpsHost")
            Dim portStr = Setup.Get("FrpsPort")
            Dim token = Setup.Get("FrpsToken")
            Dim useTlsStr = Setup.Get("UseTLS")
            Dim namePattern = Setup.Get("ProxyNamePattern")
            Dim remotePortStr = Setup.Get("FrpsRemotePort")

            If String.IsNullOrWhiteSpace(host) Then host = "frps.naids.com"
            Dim frpsPort As Integer = 7000
            Integer.TryParse(portStr, frpsPort)
            Dim useTls As Boolean = False
            Boolean.TryParse(useTlsStr, useTls)
            Dim remotePort As Integer = 25565
            Integer.TryParse(remotePortStr, remotePort)
            If String.IsNullOrWhiteSpace(namePattern) Then namePattern = "pclce-{LobbyCode}-{Username}"
            Dim proxyName = namePattern.Replace("{Username}", username).Replace("{LobbyCode}", lobbyCode)

            PublicHost = host
            PublicPort = remotePort

            Dim iniDir = IO.Path.Combine(PathTemp, "frpc")
            IO.Directory.CreateDirectory(iniDir)
            Dim iniPath = IO.Path.Combine(iniDir, "frpc.ini")

            Dim sb As New StringBuilder()
            sb.AppendLine("[common]")
            sb.AppendLine("server_addr=" & host)
            sb.AppendLine("server_port=" & frpsPort.ToString())
            If Not String.IsNullOrWhiteSpace(token) Then sb.AppendLine("token=" & token)
            If useTls Then sb.AppendLine("tls=true")
            sb.AppendLine()
            sb.AppendLine("[minecraft]")
            sb.AppendLine("type=tcp")
            sb.AppendLine("local_ip=127.0.0.1")
            sb.AppendLine("local_port=" & worldPort.ToString())
            sb.AppendLine("remote_port=" & remotePort.ToString())
            sb.AppendLine("name=" & proxyName)

            IO.File.WriteAllText(iniPath, sb.ToString(), Encoding.UTF8)
            Log("[FRP] Config: " & iniPath)

            Dim psi As New ProcessStartInfo()
            psi.FileName = GetFrpcPath()
            psi.Arguments = "-c """ & iniPath & """"
            psi.UseShellExecute = False
            psi.RedirectStandardOutput = True
            psi.RedirectStandardError = True
            psi.CreateNoWindow = True
            Try
                psi.StandardOutputEncoding = Encoding.UTF8
                psi.StandardErrorEncoding = Encoding.UTF8
            Catch
            End Try

            _proc = New Process()
            _proc.StartInfo = psi
            _proc.EnableRaisingEvents = True
            AddHandler _proc.Exited, Sub() IsRunning = False
            Log("[FRP] Args: " & psi.Arguments)
            Dim started = _proc.Start()
            Log("[FRP] Started: " & started.ToString())
            If Not started Then Return False

            Dim ok As Boolean = False
            Dim startDeadline = DateTime.UtcNow.AddSeconds(20)
            Dim errTask = Task.Run(Async Function()
                                       While Not _proc.HasExited
                                           Dim el = Await _proc.StandardError.ReadLineAsync()
                                           If el Is Nothing Then Exit While
                                           Log("[FRP][stderr] " & el)
                                       End While
                                   End Function)
            While DateTime.UtcNow < startDeadline
                If _proc.HasExited Then Exit While
                Dim line = Await _proc.StandardOutput.ReadLineAsync()
                If line Is Nothing Then Exit While
                Log("[FRP][stdout] " & line)
                If line.ToLower().Contains("隧道启动成功") OrElse line.ToLower().Contains("connected") OrElse line.Contains("client/control.go") OrElse line.Contains("proxy_manager.go") Then
                    ok = True
                    Exit While
                End If
                If line.ToLower().Contains("error") OrElse line.ToLower().Contains("failed") Then
                    Log("[FRP] Failure detected")
                    ok = False
                    Exit While
                End If
            End While

            IsRunning = ok AndAlso Not _proc.HasExited
            Log("[FRP] Result: " & IsRunning.ToString())
            Return IsRunning
        Catch ex As Exception
            Log(ex, "[FRP] 启动 frpc 失败", LogLevel.Hint)
            Return False
        End Try
    End Function

    Public Async Function StopAsync() As Task
        Await Task.Yield()
        Try
            If _proc IsNot Nothing AndAlso Not _proc.HasExited Then
                Try
                    _proc.Kill()
                Catch
                End Try
            End If
        Catch
        Finally
            IsRunning = False
        End Try
    End Function

    Public Async Function StartWithServerAsync(serverAddr As String, serverPort As Integer, remotePort As Integer, localPort As Integer, proxyName As String, Optional tomlConfig As String = Nothing) As Task(Of Boolean)
        Await Task.Yield()
        Try
            Log("[FRP] StartWithServerAsync")
            PublicHost = serverAddr
            PublicPort = remotePort

            Dim iniDir = IO.Path.Combine(PathTemp, "frpc")
            IO.Directory.CreateDirectory(iniDir)

            Dim cfgPath As String
            Dim useToml As Boolean = Not String.IsNullOrWhiteSpace(tomlConfig)
            If useToml Then
                cfgPath = IO.Path.Combine(iniDir, "frpc.toml")
                IO.File.WriteAllText(cfgPath, tomlConfig, Encoding.UTF8)
            Else
                cfgPath = IO.Path.Combine(iniDir, "frpc.ini")
                Dim sb As New StringBuilder()
                sb.AppendLine("[common]")
                sb.AppendLine("server_addr=" & serverAddr)
                sb.AppendLine("server_port=" & serverPort.ToString())
                sb.AppendLine()
                sb.AppendLine("[minecraft]")
                sb.AppendLine("type=tcp")
                sb.AppendLine("local_ip=127.0.0.1")
                sb.AppendLine("local_port=" & localPort.ToString())
                sb.AppendLine("remote_port=" & remotePort.ToString())
                sb.AppendLine("name=" & proxyName)
                IO.File.WriteAllText(cfgPath, sb.ToString(), Encoding.UTF8)
            End If
            Log("[FRP] Config: " & cfgPath)

            Dim psi As New ProcessStartInfo()
            psi.FileName = GetFrpcPath()
            psi.Arguments = "-c \"" & cfgPath & "\""
            psi.UseShellExecute = False
            psi.RedirectStandardOutput = True
            psi.RedirectStandardError = True
            psi.CreateNoWindow = True
            Try
                psi.StandardOutputEncoding = Encoding.UTF8
                psi.StandardErrorEncoding = Encoding.UTF8
            Catch
            End Try

            _proc = New Process()
            _proc.StartInfo = psi
            _proc.EnableRaisingEvents = True
            AddHandler _proc.Exited, Sub() IsRunning = False
            Log("[FRP] Args: " & psi.Arguments)
            Dim started = _proc.Start()
            Log("[FRP] Started: " & started.ToString())
            If Not started Then Return False

            Dim ok As Boolean = False
            Dim startDeadline = DateTime.UtcNow.AddSeconds(20)
            Dim errTask = Task.Run(Async Function()
                                       While Not _proc.HasExited
                                           Dim el = Await _proc.StandardError.ReadLineAsync()
                                           If el Is Nothing Then Exit While
                                           Log("[FRP][stderr] " & el)
                                       End While
                                   End Function)
            While DateTime.UtcNow < startDeadline
                If _proc.HasExited Then Exit While
                Dim line = Await _proc.StandardOutput.ReadLineAsync()
                If line Is Nothing Then Exit While
                Log("[FRP][stdout] " & line)
                If line.ToLower().Contains("start proxy success") OrElse line.ToLower().Contains("connected") OrElse line.Contains("隧道启动成功") OrElse line.Contains("client/control.go") OrElse line.Contains("proxy_manager.go") Then
                    ok = True
                    Exit While
                End If
                If line.ToLower().Contains("error") OrElse line.ToLower().Contains("failed") Then
                    Log("[FRP] Failure detected")
                    ok = False
                    Exit While
                End If
            End While

            IsRunning = ok AndAlso Not _proc.HasExited
            Log("[FRP] Result: " & IsRunning.ToString())
            Return IsRunning
        Catch ex As Exception
            Log(ex, "[FRP] 按服务端配置启动 frpc 失败", LogLevel.Hint)
            Return False
        End Try
    End Function
    Public Async Function StartWithStellarAsync(serverAddr As String, remotePort As Integer, token As String, tunnelId As Integer) As Task(Of Boolean)
        Await Task.Yield()
        Try
            Log("[FRP] StartWithStellarAsync")
            PublicHost = serverAddr
            PublicPort = remotePort

            Dim psi As New ProcessStartInfo()
            psi.FileName = GetFrpcPath()
            psi.Arguments = "-u " & token & " -t " & tunnelId.ToString()
            psi.UseShellExecute = False
            psi.RedirectStandardOutput = True
            psi.RedirectStandardError = True
            psi.CreateNoWindow = True
            Try
                psi.StandardOutputEncoding = Encoding.UTF8
                psi.StandardErrorEncoding = Encoding.UTF8
            Catch
            End Try

            _proc = New Process()
            _proc.StartInfo = psi
            _proc.EnableRaisingEvents = True
            AddHandler _proc.Exited, Sub() IsRunning = False
            Log("[FRP] Args: " & psi.Arguments)
            Dim started = _proc.Start()
            Log("[FRP] Started: " & started.ToString())
            If Not started Then Return False

            Dim ok As Boolean = False
            Dim startDeadline = DateTime.UtcNow.AddSeconds(20)
            Dim errTask = Task.Run(Async Function()
                                       While Not _proc.HasExited
                                           Dim el = Await _proc.StandardError.ReadLineAsync()
                                           If el Is Nothing Then Exit While
                                           Log("[FRP][stderr] " & el)
                                       End While
                                   End Function)
            While DateTime.UtcNow < startDeadline
                If _proc.HasExited Then Exit While
                Dim line = Await _proc.StandardOutput.ReadLineAsync()
                If line Is Nothing Then Exit While
                Log("[FRP][stdout] " & line)
                If line.ToLower().Contains("start proxy success") OrElse line.ToLower().Contains("connected") OrElse line.Contains("隧道启动成功") OrElse line.Contains("client/control.go") OrElse line.Contains("proxy_manager.go") Then
                    ok = True
                    Exit While
                End If
                If line.ToLower().Contains("error") OrElse line.ToLower().Contains("failed") Then
                    ok = False
                    Exit While
                End If
            End While

            IsRunning = ok AndAlso Not _proc.HasExited
            Log("[FRP] Result: " & IsRunning.ToString())
            Return IsRunning
        Catch ex As Exception
            Log(ex, "[FRP] 启动 StellarFrpc 失败", LogLevel.Hint)
            Return False
        End Try
    End Function
End Module