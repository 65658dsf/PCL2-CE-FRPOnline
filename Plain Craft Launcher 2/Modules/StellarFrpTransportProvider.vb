Imports PCL.Core.App
Imports Newtonsoft.Json.Linq

Public Class StellarFrpTransportProvider
    Public Property CurrentPublicAddress As String

    Public Async Function StartAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Boolean)
        Await Task.Yield()
        Try
            If Not ModLink.IsStellarFrpcInstalled() Then
                ModLink.StellarFrpDownloadClient()
                Dim waitDeadline = DateTime.UtcNow.AddSeconds(60)
                While DateTime.UtcNow < waitDeadline AndAlso Not ModLink.IsStellarFrpcInstalled()
                    Await Task.Delay(1000)
                End While
                If Not ModLink.IsStellarFrpcInstalled() Then
                    Hint("StellarFrpc 依赖下载失败", HintType.Critical)
                    Return False
                End If
            End If

            Dim token = Config.Link.StellarToken
            If String.IsNullOrWhiteSpace(token) Then
                Dim auth As New StellarAuthorizeServer()
                token = Await auth.StartAuthorizeAsync()
                If String.IsNullOrWhiteSpace(token) Then
                    Hint("StellarFrp 授权失败或已取消", HintType.Critical)
                    Return False
                End If
                Config.Link.StellarToken = token
            End If

            Dim nodes = StellarFrpApi.GetNodesV2(token)
            Dim candidate = nodes.Where(Function(n) n.Status = 1 AndAlso n.AllowedTypes IsNot Nothing AndAlso n.AllowedTypes.Contains("TCP")).FirstOrDefault()
            If candidate IsNot Nothing Then Setup.Set("LinkSelectedNodeId", candidate.Id)

            Dim list = StellarFrpApi.GetProxyList(token)
            If list Is Nothing OrElse list.Count = 0 Then
                Hint("未找到可用隧道，请先在 Stellar 控制台创建或稍后重试", HintType.Critical)
                Return False
            End If

            Dim id As Integer = Val(list(0)("Id"))
            Dim detail As StellarFrpApi.ProxyDetail = Nothing
            Try
                detail = StellarFrpApi.GetProxyDetail(token, id)
            Catch
                detail = New StellarFrpApi.ProxyDetail()
            End Try
            If String.IsNullOrWhiteSpace(detail.Link) Then
                Try
                    Dim linkTuple = StellarFrpApi.GetProxyLink(token, id)
                    If Not String.IsNullOrWhiteSpace(linkTuple.Item1) Then detail.Link = linkTuple.Item1
                    If detail.RemotePort <= 0 AndAlso linkTuple.Item2 > 0 Then detail.RemotePort = linkTuple.Item2
                Catch
                End Try
            End If

            Dim pattern = Config.Link.ProxyNamePattern
            If String.IsNullOrWhiteSpace(pattern) Then pattern = "pclce-{LobbyCode}-{Username}"
            Dim proxyName = pattern.Replace("{Username}", username).Replace("{LobbyCode}", lobbyCode)

            Dim ok = Await FrpController.StartWithStellarAsync(detail.ServerAddr, detail.RemotePort, token, id)
            If ok Then
                CurrentPublicAddress = FrpController.PublicHost & ":" & FrpController.PublicPort.ToString()
                Return True
            Else
                Hint("StellarFrp 隧道启动失败，请稍后重试或更换节点", HintType.Critical)
                Return False
            End If
        Catch ex As Exception
            Log(ex, "[StellarFrp] 启动失败", LogLevel.Hint)
            Hint("StellarFrp 流程出错：" & ex.Message, HintType.Critical)
            Return False
        End Try
    End Function
End Class

Public Class StellarFrpTransportCreateHelper
    Private Shared Function ParsePortRange(range As String) As Tuple(Of Integer, Integer)
        If String.IsNullOrWhiteSpace(range) Then Return New Tuple(Of Integer, Integer)(10000, 60000)
        Dim parts = range.Split("-"c)
        If parts.Length <> 2 Then Return New Tuple(Of Integer, Integer)(10000, 60000)
        Dim minV As Integer = 10000, maxV As Integer = 60000
        Integer.TryParse(parts(0), minV)
        Integer.TryParse(parts(1), maxV)
        Return New Tuple(Of Integer, Integer)(minV, maxV)
    End Function

    Public Shared Async Function StartCreateAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Tuple(Of Boolean, String))
        Await Task.Yield()
        Try
            If Not ModLink.IsStellarFrpcInstalled() Then
                ModLink.StellarFrpDownloadClient()
                Dim waitDeadline = DateTime.UtcNow.AddSeconds(60)
                While DateTime.UtcNow < waitDeadline AndAlso Not ModLink.IsStellarFrpcInstalled()
                    Await Task.Delay(1000)
                End While
                If Not ModLink.IsStellarFrpcInstalled() Then
                    Return New Tuple(Of Boolean, String)(False, "依赖下载失败")
                End If
            End If

            Dim token = Config.Link.StellarToken
            If String.IsNullOrWhiteSpace(token) Then
                Dim auth As New StellarAuthorizeServer()
                token = Await auth.StartAuthorizeAsync()
                If String.IsNullOrWhiteSpace(token) Then
                    Return New Tuple(Of Boolean, String)(False, "授权失败或已取消")
                End If
                Config.Link.StellarToken = token
            End If

            Dim nodes = StellarFrpApi.GetNodesV2(token)
            Dim selectedId = Config.Link.SelectedNodeId
            Dim candidate = If(selectedId > 0, nodes.FirstOrDefault(Function(n) n.Id = selectedId), nodes.FirstOrDefault(Function(n) n.Status = 1 AndAlso n.AllowedTypes IsNot Nothing AndAlso n.AllowedTypes.Contains("TCP")))
            If candidate Is Nothing Then
                Return New Tuple(Of Boolean, String)(False, "未找到可用节点")
            End If
            Setup.Set("LinkSelectedNodeId", candidate.Id)

            Dim pr = ParsePortRange(candidate.PortRange)
            Dim rnd As New Random()
            Dim remotePort As Integer = rnd.Next(pr.Item1, pr.Item2)

            Dim pattern = Config.Link.ProxyNamePattern
            If String.IsNullOrWhiteSpace(pattern) Then pattern = "pclce-{LobbyCode}-{Username}"
            Dim proxyName = pattern.Replace("{Username}", username).Replace("{LobbyCode}", lobbyCode)

            Dim payload As New JObject(
                New JProperty("nodeId", candidate.Id),
                New JProperty("proxyName", proxyName),
                New JProperty("proxyType", "tcp"),
                New JProperty("localIp", "127.0.0.1"),
                New JProperty("localPort", localPort),
                New JProperty("remotePort", remotePort),
                New JProperty("domain", ""),
                New JProperty("hostHeaderRewrite", ""),
                New JProperty("headerXFromWhere", ""),
                New JProperty("proxyProtocolVersion", ""),
                New JProperty("useEncryption", True),
                New JProperty("useCompression", True),
                New JProperty("autoTls", False),
                New JProperty("crtBase64", ""),
                New JProperty("keyBase64", "")
            )

            Dim id As Integer = 0
            Try
                Dim existsList = StellarFrpApi.GetProxyList(token, page:=1, pageSize:=50)
                If existsList IsNot Nothing Then
                    For Each item As JObject In existsList
                        Dim pn = If(item("ProxyName") IsNot Nothing, item("ProxyName").ToString(), item("proxyName")?.ToString())
                        If pn = proxyName Then
                            id = Val(If(item("Id") IsNot Nothing, item("Id").ToString(), item("id")?.ToString()))
                            Exit For
                        End If
                    Next
                End If
            Catch
            End Try
            If id = 0 Then id = StellarFrpApi.CreateProxy(token, payload)
            If id <= 0 Then
                Return New Tuple(Of Boolean, String)(False, "创建隧道失败")
            End If
            Config.Link.CurrentTunnelId = id

            Dim detail As StellarFrpApi.ProxyDetail = Nothing
            Try
                detail = StellarFrpApi.GetProxyDetail(token, id)
            Catch
                detail = New StellarFrpApi.ProxyDetail()
            End Try
            If String.IsNullOrWhiteSpace(detail.Link) Then
                Try
                    Dim linkTuple = StellarFrpApi.GetProxyLink(token, id)
                    If Not String.IsNullOrWhiteSpace(linkTuple.Item1) Then detail.Link = linkTuple.Item1
                    If detail.RemotePort <= 0 AndAlso linkTuple.Item2 > 0 Then detail.RemotePort = linkTuple.Item2
                Catch
                End Try
            End If
            Dim remoteToUse As Integer = If(detail.RemotePort > 0, detail.RemotePort, remotePort)
            Dim hostFromLink As String = ""
            Dim portFromLink As Integer = 0
            If Not String.IsNullOrWhiteSpace(detail.Link) Then
                Dim idx As Integer = detail.Link.LastIndexOf(":"c)
                If idx > 0 AndAlso idx < detail.Link.Length - 1 Then
                    hostFromLink = detail.Link.Substring(0, idx)
                    Integer.TryParse(detail.Link.Substring(idx + 1), portFromLink)
                    If portFromLink > 0 Then remoteToUse = portFromLink
                End If
            End If
            Dim hostFromToml As String = ""
            If String.IsNullOrWhiteSpace(hostFromLink) AndAlso Not String.IsNullOrWhiteSpace(detail.ConfigToml) Then
                Try
                    Dim m = System.Text.RegularExpressions.Regex.Match(detail.ConfigToml, "serverAddr\s*=\s*""([^""]+)""")
                    If m.Success Then hostFromToml = m.Groups(1).Value
                Catch
                End Try
            End If
            Dim ok As Boolean
            Dim hostForStart As String = If(Not String.IsNullOrWhiteSpace(hostFromLink), hostFromLink, If(Not String.IsNullOrWhiteSpace(hostFromToml), hostFromToml, detail.ServerAddr))
            ok = Await FrpController.StartWithStellarAsync(hostForStart, remoteToUse, token, id)
            If Not ok AndAlso Not String.IsNullOrWhiteSpace(detail.ServerAddr) AndAlso detail.ServerPort > 0 AndAlso Not String.IsNullOrWhiteSpace(detail.ConfigToml) Then
                ok = Await FrpController.StartWithServerAsync(detail.ServerAddr, detail.ServerPort, remoteToUse, localPort, proxyName, detail.ConfigToml)
            End If
            If ok Then
                Dim host = If(String.IsNullOrWhiteSpace(FrpController.PublicHost), If(Not String.IsNullOrWhiteSpace(hostFromLink), hostFromLink, If(Not String.IsNullOrWhiteSpace(hostFromToml), hostFromToml, detail.ServerAddr)), FrpController.PublicHost)
                Dim addr = If(Not String.IsNullOrWhiteSpace(detail.Link), detail.Link, host & ":" & If(FrpController.PublicPort > 0, FrpController.PublicPort, remoteToUse))
                If String.IsNullOrWhiteSpace(addr) OrElse Not addr.Contains(":") OrElse addr.StartsWith(":") Then
                    Dim fallbackHost = If(Not String.IsNullOrWhiteSpace(hostForStart), hostForStart, If(Not String.IsNullOrWhiteSpace(FrpController.PublicHost), FrpController.PublicHost, ""))
                    addr = fallbackHost & ":" & remoteToUse
                End If
                Return New Tuple(Of Boolean, String)(True, addr)
            End If
            Return New Tuple(Of Boolean, String)(False, "隧道启动失败")
        Catch ex As Exception
            Return New Tuple(Of Boolean, String)(False, ex.Message)
        End Try
    End Function
End Class