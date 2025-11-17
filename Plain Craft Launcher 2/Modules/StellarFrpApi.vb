Imports Newtonsoft.Json.Linq
Imports PCL.Core.App

Public Class StellarFrpApi
    Private Shared ReadOnly BaseUrl As String = "https://api.stellarfrp.top"

    Public Class StellarNode
        Public Property Id As Integer
        Public Property Name As String
        Public Property Region As String
        Public Property Bandwidth As Integer
        Public Property Status As Integer
        Public Property AllowedTypes As List(Of String)
        Public Property PortRange As String
    End Class

    Public Class ProxyDetail
        Public Property ServerAddr As String
        Public Property ServerPort As Integer
        Public Property RemotePort As Integer
        Public Property ProxyName As String
        Public Property LocalPort As Integer
        Public Property NodeId As Integer
        Public Property ConfigToml As String
        Public Property Link As String
    End Class

    Public Class UserInfo
        Public Property Username As String
        Public Property Email As String
        Public Property Status As Integer
        Public Property GroupName As String
    End Class

    Private Shared Function AuthHeaders(token As String) As Dictionary(Of String, String)
        Dim headers As New Dictionary(Of String, String)
        If Not String.IsNullOrWhiteSpace(token) Then headers("Authorization") = token
        headers("Accept") = "application/json"
        Return headers
    End Function

    Public Shared Function GetNodesV2(token As String) As List(Of StellarNode)
        Dim url = BaseUrl & "/api/v1/nodes/get"
        Dim res = ModNet.NetRequestOnce(url, "GET", Nothing, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        Dim nodes As New List(Of StellarNode)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim data = TryCast(j("data"), JObject)
        If data IsNot Nothing Then
            For Each prop In data.Properties()
                Dim obj = CType(prop.Value, JObject)
                Dim n As New StellarNode()
                Dim nodeName = obj("NodeName")?.ToString()
                n.Id = If(obj("ID") IsNot Nothing, Val(obj("ID")), Val(prop.Name))
                n.Name = nodeName
                n.Status = Val(obj("Status"))
                n.AllowedTypes = (CType(obj("AllowedTypes"), JArray))?.Select(Function(x) x.ToString()).ToList()
                n.PortRange = obj("PortRange")?.ToString()
                nodes.Add(n)
            Next
        End If
        Return nodes
    End Function

    Public Shared Function GetProxyList(token As String, Optional page As Integer = 1, Optional pageSize As Integer = 10) As JArray
        Dim url = BaseUrl & $"/api/v1/proxy/get?page={page}&page_size={pageSize}"
        Dim res = ModNet.NetRequestOnce(url, "GET", Nothing, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Return CType(j("data"), JArray)
    End Function

    Public Shared Function GetProxyDetail(token As String, id As Integer) As ProxyDetail
        Dim url = BaseUrl & "/api/v1/proxy/get"
        Dim body As String = New JObject(New JProperty("id", id)).ToString()
        Log("[StellarFrp][Detail] url=" & url & " body=" & body)
        Dim res = ModNet.NetRequestOnce(url, "POST", body, "application/json", Headers:=AuthHeaders(token))
        Log("[StellarFrp][Detail] response=" & res)
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim data = CType(j("data"), JObject)
        Dim detail As New ProxyDetail()

        detail.ServerAddr = data("serverAddr")?.ToString()
        detail.ServerPort = Val(data("serverPort"))
        detail.RemotePort = Val(data("remotePort"))
        detail.ProxyName = data("ProxyName")?.ToString()
        detail.LocalPort = Val(data("LocalPort"))
        detail.NodeId = Val(data("NodeId"))

        Dim tunnel = CType(data("tunnel"), JObject)
        If tunnel IsNot Nothing Then
            For Each p In tunnel.Properties()
                Dim seg = CType(p.Value, JObject)
                If seg("data") IsNot Nothing Then
                    detail.ConfigToml = seg("data").ToString()
                    Exit For
                End If
            Next
        End If

        Dim topTunnel As JObject = Nothing
        If data IsNot Nothing Then topTunnel = TryCast(data("tunnel"), JObject)
        If topTunnel Is Nothing Then topTunnel = TryCast(j("tunnel"), JObject)

        If CType(topTunnel, Object) IsNot Nothing Then
            For Each p In topTunnel.Properties()
                Dim seg = TryCast(p.Value, JObject)
                If seg Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(detail.ConfigToml) AndAlso seg.ContainsKey("data") Then
                    detail.ConfigToml = seg("data").ToString()
                End If
                If String.IsNullOrWhiteSpace(detail.Link) AndAlso seg.ContainsKey("Link") Then
                    detail.Link = seg("Link").ToString()
                End If
                If detail.RemotePort <= 0 AndAlso seg.ContainsKey("RemotePort") Then
                    Dim rp As Integer = 0
                    Integer.TryParse(seg("RemotePort").ToString(), rp)
                    If rp > 0 Then detail.RemotePort = rp
                End If
                If detail.LocalPort <= 0 AndAlso seg.ContainsKey("LocalPort") Then
                    Dim lp As Integer = 0
                    Integer.TryParse(seg("LocalPort").ToString(), lp)
                    If lp > 0 Then detail.LocalPort = lp
                End If
                If String.IsNullOrWhiteSpace(detail.ProxyName) AndAlso seg.ContainsKey("ProxyName") Then
                    detail.ProxyName = seg("ProxyName").ToString()
                End If
                If detail.NodeId <= 0 AndAlso seg.ContainsKey("NodeId") Then
                    Dim nid As Integer = 0
                    Integer.TryParse(seg("NodeId").ToString(), nid)
                    If nid > 0 Then detail.NodeId = nid
                End If
                Exit For
            Next
        End If

        If (String.IsNullOrWhiteSpace(detail.ServerAddr) OrElse detail.ServerPort <= 0) AndAlso Not String.IsNullOrWhiteSpace(detail.ConfigToml) Then
            Try
                Dim m1 = System.Text.RegularExpressions.Regex.Match(detail.ConfigToml, "serverAddr\s*=\s*""([^""]+)""")
                If m1.Success Then detail.ServerAddr = m1.Groups(1).Value
                Dim m2 = System.Text.RegularExpressions.Regex.Match(detail.ConfigToml, "serverPort\s*=\s*([0-9]+)")
                If m2.Success Then
                    Dim sp As Integer = 0
                    Integer.TryParse(m2.Groups(1).Value, sp)
                    If sp > 0 Then detail.ServerPort = sp
                End If
            Catch
            End Try
        End If

        Dim tObj = TryCast(data("tunnel"), JObject)
        If tObj IsNot Nothing Then
            For Each p In tObj.Properties()
                Dim seg = TryCast(p.Value, JObject)
                If seg IsNot Nothing Then
                    Try
                        detail.Link = seg("Link").ToString()
                        If Not String.IsNullOrWhiteSpace(detail.Link) Then Exit For
                    Catch
                    End Try
                End If
            Next
        End If
        Return detail
    End Function

    Public Shared Function GetProxyLink(token As String, id As Integer) As Tuple(Of String, Integer)
        Dim url = BaseUrl & "/api/v1/proxy/get"
        Dim body As String = New JObject(New JProperty("id", id)).ToString()
        Dim res = ModNet.NetRequestOnce(url, "POST", body, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim hostPort As String = ""
        Dim port As Integer = 0
        Dim topTunnel = TryCast(j("tunnel"), JObject)
        If topTunnel IsNot Nothing Then
            For Each p In topTunnel.Properties()
                Dim seg = TryCast(p.Value, JObject)
                If seg Is Nothing Then Continue For
                If seg.ContainsKey("Link") Then hostPort = seg("Link").ToString()
                If seg.ContainsKey("RemotePort") Then Integer.TryParse(seg("RemotePort").ToString(), port)
                Exit For
            Next
        End If
        Return New Tuple(Of String, Integer)(hostPort, port)
    End Function

    Public Shared Function GetUserInfo(token As String) As UserInfo
        Dim url = BaseUrl & "/api/v1/users/info"
        Dim res = ModNet.NetRequestOnce(url, "GET", Nothing, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim d = CType(j("data"), JObject)
        If d Is Nothing Then Return Nothing
        Dim ui As New UserInfo()
        ui.Username = d("Username")?.ToString()
        ui.Email = d("Email")?.ToString()
        ui.Status = Val(d("Status"))
        ui.GroupName = d("GroupName")?.ToString()
        Return ui
    End Function

    Public Shared Function CreateProxy(token As String, payload As JObject) As Integer
        Dim tryUrls = New List(Of String) From {
            BaseUrl & "/api/v1/proxy/create"
        }
        For Each url In tryUrls
            Try
                Log("[StellarFrp][Create] url=" & url & " payload=" & payload.ToString())
                Dim res = ModNet.NetRequestOnce(url, "POST", payload.ToString(), "application/json", Headers:=AuthHeaders(token))
                Log("[StellarFrp][Create] response=" & res)
                Dim j As JObject = GetJson(res)
                Dim codeStr = j("code")?.ToString()
                If codeStr = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
                If codeStr = "400" AndAlso (j("msg")?.ToString()?.Contains("同名隧道") = True) Then
                    Dim pn = payload("proxyName")?.ToString()
                    If Not String.IsNullOrWhiteSpace(pn) Then
                        Try
                            Dim page As Integer = 1
                            Dim pageSize As Integer = 200
                            While True
                                Dim list = GetProxyList(token, page, pageSize)
                                If list Is Nothing OrElse list.Count = 0 Then Exit While
                                For Each item As JObject In list
                                    Dim name As String = Nothing
                                    If item.ContainsKey("ProxyName") Then
                                        name = item("ProxyName").ToString()
                                    ElseIf item.ContainsKey("proxyName") Then
                                        name = item("proxyName").ToString()
                                    End If
                                    If name = pn Then
                                        Dim idStr0 As String = Nothing
                                        If item.ContainsKey("Id") Then
                                            idStr0 = item("Id").ToString()
                                        ElseIf item.ContainsKey("id") Then
                                            idStr0 = item("id").ToString()
                                        End If
                                        Dim id0 As Integer = 0
                                        If Not String.IsNullOrWhiteSpace(idStr0) Then Integer.TryParse(idStr0, id0)
                                        If id0 > 0 Then Return id0
                                    End If
                                Next
                                page += 1
                                If list.Count < pageSize Then Exit While
                            End While
                        Catch
                        End Try
                    End If
                End If
                Dim d = CType(j("data"), JObject)
                If d IsNot Nothing Then
                    Dim idStr = d("Id")?.ToString()
                    If String.IsNullOrWhiteSpace(idStr) Then idStr = d("id")?.ToString()
                    Dim id As Integer = 0
                    Integer.TryParse(idStr, id)
                    If id > 0 Then Return id
                End If
            Catch
            End Try
        Next
        Return 0
    End Function

    Public Shared Function DeleteProxy(token As String, id As Integer) As Boolean
        Dim url = BaseUrl & "/api/v1/proxy/delete"
        Dim body As String = New JObject(New JProperty("id", id)).ToString()
        Dim res = ModNet.NetRequestOnce(url, "POST", body, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Return j("code")?.ToString() = "200"
    End Function

End Class
