Imports System.Runtime.InteropServices
Imports Newtonsoft.Json.Linq
Imports PCL.Core.App
Imports PCL.Core.IO
Imports PCL.Core.Link
Imports PCL.Core.Link.EasyTier
Imports PCL.Core.Link.Lobby
Imports PCL.Core.Link.Natayark.NatayarkProfileManager
Imports PCL.Core.Utils.OS

Public Module ModLink

#Region "端口查找"
    Public Class PortFinder
        ' 定义需要的结构和常量
        <StructLayout(LayoutKind.Sequential)>
        Public Structure MIB_TCPROW_OWNER_PID
            Public dwState As Integer
            Public dwLocalAddr As Integer
            Public dwLocalPort As Integer
            Public dwRemoteAddr As Integer
            Public dwRemotePort As Integer
            Public dwOwningPid As Integer
        End Structure

        <DllImport("iphlpapi.dll", SetLastError:=True)>
        Public Shared Function GetExtendedTcpTable(
        ByVal pTcpTable As IntPtr,
        ByRef dwOutBufLen As Integer,
        ByVal bOrder As Boolean,
        ByVal ulAf As Integer,
        ByVal TableClass As Integer,
        ByVal reserved As Integer) As Integer
        End Function

        Public Shared Function GetProcessPort(ByVal dwProcessId As Integer) As List(Of Integer)
            Dim ports As New List(Of Integer)
            Dim tcpTable As IntPtr = IntPtr.Zero
            Dim dwSize As Integer = 0
            Dim dwRetVal As Integer

            If dwProcessId = 0 Then
                Return ports
            End If

            dwRetVal = GetExtendedTcpTable(IntPtr.Zero, dwSize, True, 2, 3, 0)
            If dwRetVal <> 0 AndAlso dwRetVal <> 122 Then ' 122 表示缓冲区不足
                Return ports
            End If

            tcpTable = Marshal.AllocHGlobal(dwSize)
            Try
                If GetExtendedTcpTable(tcpTable, dwSize, True, 2, 3, 0) <> 0 Then
                    Return ports
                End If

                Dim tablePtr As IntPtr = tcpTable
                Dim dwNumEntries As Integer = Marshal.ReadInt32(tablePtr)
                tablePtr = IntPtr.Add(tablePtr, 4)

                For i As Integer = 0 To dwNumEntries - 1
                    Dim row As MIB_TCPROW_OWNER_PID = Marshal.PtrToStructure(Of MIB_TCPROW_OWNER_PID)(tablePtr)
                    If row.dwOwningPid = dwProcessId Then
                        ports.Add(row.dwLocalPort >> 8 Or (row.dwLocalPort And &HFF) << 8) ' 转换端口号
                    End If
                    tablePtr = IntPtr.Add(tablePtr, Marshal.SizeOf(Of MIB_TCPROW_OWNER_PID)())
                Next
            Finally
                Marshal.FreeHGlobal(tcpTable)
            End Try

            Return ports
        End Function
    End Class
#End Region

#Region "Minecraft 实例探测"
    Public Async Function MCInstanceFinding() As Task(Of List(Of Tuple(Of Integer, McPingResult, String)))
        'Java 进程 PID 查询
        Dim PIDLookupResult As New List(Of String)
        Dim JavaNames As New List(Of String)
        JavaNames.Add("java")
        JavaNames.Add("javaw")

        For Each TargetJava In JavaNames
            Dim JavaProcesses As Process() = Process.GetProcessesByName(TargetJava)
            Log($"[MCDetect] 找到 {TargetJava} 进程 {JavaProcesses.Length} 个")

            If JavaProcesses Is Nothing OrElse JavaProcesses.Length = 0 Then
                Continue For
            Else
                For Each p In JavaProcesses
                    Log("[MCDetect] 检测到 Java 进程，PID: " + p.Id.ToString())
                    PIDLookupResult.Add(p.Id.ToString())
                Next
            End If
        Next

        Dim res As New List(Of Tuple(Of Integer, McPingResult, String))
        Try
            If Not PIDLookupResult.Any Then Return res
            Dim lookupList As New List(Of Tuple(Of Integer, Integer))
            For Each pid In PIDLookupResult
                Dim infos As New List(Of Tuple(Of Integer, Integer))
                Dim ports = PortFinder.GetProcessPort(Integer.Parse(pid))
                For Each port In ports
                    infos.Add(New Tuple(Of Integer, Integer)(port, pid))
                Next
                lookupList.AddRange(infos)
            Next
            Log($"[MCDetect] 获取到端口数量 {lookupList.Count}")
            '并行查找本地，超时 3s 自动放弃
            Dim checkTasks = lookupList.Select(Function(lookup) Task.Run(Async Function()
                                                                             Log($"[MCDetect] 找到疑似端口，开始验证：{lookup}")
                                                                             Using test As New McPing("127.0.0.1", lookup.Item1, 3000)
                                                                                 Dim info As McPingResult
                                                                                 Try
                                                                                     info = Await test.PingAsync()
                                                                                     Dim launcher = GetLauncherBrand(lookup.Item2)
                                                                                     If Not String.IsNullOrWhiteSpace(info.Version.Name) Then
                                                                                         Log($"[MCDetect] 端口 {lookup} 为有效 Minecraft 世界")
                                                                                         res.Add(New Tuple(Of Integer, McPingResult, String)(lookup.Item1, info, launcher))
                                                                                         Return
                                                                                     End If
                                                                                 Catch ex As Exception
                                                                                     If TypeOf ex.InnerException Is ObjectDisposedException Then
                                                                                         Log($"[McDetect] {lookup} 验证超时，已强制断开连接，将尝试旧版检测")
                                                                                     Else
                                                                                         Log(ex, $"[McDetect] {lookup} 验证出错，将尝试旧版检测")
                                                                                     End If
                                                                                 End Try
                                                                                 Try
                                                                                     info = Await test.PingOldAsync()
                                                                                     If Not String.IsNullOrWhiteSpace(info.Version.Name) Then
                                                                                         Log($"[MCDetect] 端口 {lookup} 为有效 Minecraft 世界")
                                                                                         res.Add(New Tuple(Of Integer, McPingResult, String)(lookup.Item1, info, String.Empty))
                                                                                         Return
                                                                                     End If
                                                                                 Catch ex As Exception
                                                                                     If TypeOf ex.InnerException Is ObjectDisposedException Then
                                                                                         Log($"[McDetect] {lookup} 验证超时，已强制断开连接")
                                                                                     Else
                                                                                         Log(ex, $"[McDetect] {lookup} 验证出错")
                                                                                     End If
                                                                                 End Try
                                                                             End Using
                                                                         End Function)).ToArray()
            Await Task.WhenAll(checkTasks)
        Catch ex As Exception
            Log(ex, "[MCDetect] 获取端口信息错误", LogLevel.Debug)
        End Try
        Return res
    End Function
    Public Function GetLauncherBrand(pid As Integer) As String
        Try
            Dim cmd = ProcessInterop.GetCommandLine(pid)
            If cmd.Contains("-Dminecraft.launcher.brand=") Then
                Return cmd.AfterFirst("-Dminecraft.launcher.brand=").BeforeFirst("-").TrimEnd("'", " ")
            Else
                Return cmd.AfterFirst("--versionType ").BeforeFirst("-").TrimEnd("'", " ")
            End If
        Catch ex As Exception
            Log(ex, $"[MCDetect] 检测 PID {pid} 进程的启动参数失败")
            Return ""
        End Try
    End Function
#End Region

#Region "FRP"
    Public DlFrpcLoader As LoaderCombo(Of JObject) = Nothing
    Public Function DownloadFrpc()
        Dim version = Setup.Get("FrpcVersion")
        If String.IsNullOrWhiteSpace(version) Then version = "0.61.2"
        Dim dlTargetPath As String = PathTemp + $"frp\frpc-{version}.zip"
        RunInNewThread(Sub()
                           Try
                               Dim loaders As New List(Of LoaderBase)
                               Dim address As New List(Of String)
                               address.Add($"https://staticassets.naids.com/resources/pclce/static/frp/frpc-windows-{If(IsArm64System, "arm64", "x86_64")}-v{version}.zip")
                               address.Add($"https://s3.pysio.online/pcl2-ce/static/frp/frpc-windows-{If(IsArm64System, "arm64", "x86_64")}-v{version}.zip")

                               loaders.Add(New LoaderDownload("下载 frpc", New List(Of NetFile) From {New NetFile(address.ToArray, dlTargetPath, New FileChecker(MinSize:=1024 * 64))}) With {.ProgressWeight = 15})
                               loaders.Add(New LoaderTask(Of Integer, Integer)("解压文件", Sub() ExtractFile(dlTargetPath, IO.Path.Combine(FileService.LocalDataPath, "frp", version))) With {.Block = True})
                               loaders.Add(New LoaderTask(Of Integer, Integer)("修正可执行文件布局", Sub() EnsureFrpcExecutable(version)))
                               loaders.Add(New LoaderTask(Of Integer, Integer)("清理缓存与冗余组件", Sub()
                                                                                                 File.Delete(dlTargetPath)
                                                                                                 CleanupFrpcCache(version)
                                                                                             End Sub))
                               loaders.Add(New LoaderTask(Of Integer, Integer)("记录版本", Sub() Setup.Set("FrpcVersion", version)) With {.Show = False})
                               loaders.Add(New LoaderTask(Of Integer, Integer)("刷新界面", Sub() Hint("联机组件下载完成！", HintType.Finish)) With {.Show = False})

                               DlFrpcLoader = New LoaderCombo(Of JObject)("大厅初始化", loaders)
                               DlFrpcLoader.Start()
                               LoaderTaskbarAdd(DlFrpcLoader)
                               FrmMain.BtnExtraDownload.ShowRefresh()
                               FrmMain.BtnExtraDownload.Ribble()
                           Catch ex As Exception
                               Log(ex, "[Link] 下载 frpc 依赖文件失败", LogLevel.Hint)
                               Hint("下载 frpc 依赖文件失败，请检查网络连接", HintType.Critical)
                           End Try
                       End Sub)
        Return 0
    End Function
    Public Function StellarFrpDownloadClient()
        Dim sources = New List(Of String) From {"蓝奏云", "中国移动云盘"}
        RunInNewThread(Sub()
                           Try
                               Dim arch = If(IsArm64System, "windows_arm64", "windows_amd64")
                               Dim bestName As String = Nothing
                               Dim bestVer As String = Nothing
                               Dim bestSource As String = Nothing
                               For Each src In sources
                                   Dim api = "https://resources.stellarfrp.top/api/fs/list?path=StellarCore/" & Uri.EscapeDataString(src) & "/frpc"
                                   Dim res = NetGetCodeByRequestOnce(api, Timeout:=15000)
                                   Dim j = GetJson(res)
                                   Dim content = CType(j("data")("content"), JArray)
                                   For Each item As JObject In content
                                       Dim name = item("name")?.ToString()
                                       If String.IsNullOrWhiteSpace(name) Then Continue For
                                       If name.StartsWith("StellarFrpc_") AndAlso name.Contains(arch) AndAlso name.ToLower().EndsWith(".zip") Then
                                           Dim parts = name.Split("_"c)
                                           If parts.Length >= 3 Then
                                               Dim ver = parts(1)
                                               If String.IsNullOrWhiteSpace(bestVer) OrElse String.Compare(ver, bestVer, StringComparison.OrdinalIgnoreCase) > 0 Then
                                                   bestVer = ver
                                                   bestName = name
                                                   bestSource = src
                                               End If
                                           End If
                                       End If
                                   Next
                               Next
                               If String.IsNullOrWhiteSpace(bestName) Then
                                   Hint("未在资源列表中找到合适的 frpc 构建", HintType.Critical)
                                   Exit Sub
                               End If
                               Dim dlTargetPath As String = PathTemp + $"frp\{bestName}"
                               Dim loaders As New List(Of LoaderBase)
                               Dim addresses As New List(Of String)
                               addresses.Add("https://resources.stellarfrp.top/d/StellarCore/" & Uri.EscapeDataString(bestSource) & "/frpc/" & Uri.EscapeDataString(bestName))
                               Dim otherSource As String = If(bestSource = "蓝奏云", "中国移动云盘", "蓝奏云")
                               addresses.Add("https://resources.stellarfrp.top/d/StellarCore/" & Uri.EscapeDataString(otherSource) & "/frpc/" & Uri.EscapeDataString(bestName))
                               loaders.Add(New LoaderDownload("下载 frpc", New List(Of NetFile) From {New NetFile(addresses.ToArray, dlTargetPath, New FileChecker(MinSize:=1024 * 64))}) With {.ProgressWeight = 15})
                               loaders.Add(New LoaderTask(Of Integer, Integer)("解压文件", Sub() ExtractFile(dlTargetPath, IO.Path.Combine(FileService.LocalDataPath, "frp", bestVer))) With {.Block = True})
                               loaders.Add(New LoaderTask(Of Integer, Integer)("修正可执行文件布局", Sub() EnsureFrpcExecutable(bestVer)))
                               loaders.Add(New LoaderTask(Of Integer, Integer)("清理缓存与冗余组件", Sub()
                                                                                            File.Delete(dlTargetPath)
                                                                                            CleanupFrpcCache(bestVer)
                                                                                        End Sub))
                               loaders.Add(New LoaderTask(Of Integer, Integer)("刷新界面", Sub() Hint("联机组件下载完成！", HintType.Finish)) With {.Show = False})

                               DlFrpcLoader = New LoaderCombo(Of JObject)("大厅初始化", loaders)
                               DlFrpcLoader.Start()
                               LoaderTaskbarAdd(DlFrpcLoader)
                               FrmMain.BtnExtraDownload.ShowRefresh()
                               FrmMain.BtnExtraDownload.Ribble()
                               Setup.Set("FrpcVersion", bestVer)
                           Catch ex As Exception
                               Log(ex, "[Link] 从 Stellar 资源下载 frpc 失败", LogLevel.Hint)
                               Hint("下载 frpc 依赖文件失败，请检查网络连接", HintType.Critical)
                           End Try
                       End Sub)
        Return 0
    End Function
    Public Function IsStellarFrpcInstalled() As Boolean
        Try
            Dim version = Setup.Get("FrpcVersion")
            If String.IsNullOrWhiteSpace(version) Then version = "0.61.2"
            Dim target = IO.Path.Combine(FileService.LocalDataPath, "frp", version, "StellarFrpc.exe")
            Return IO.File.Exists(target)
        Catch
            Return False
        End Try
    End Function
    Private Sub CleanupFrpcCache(currentVersion As String)
        Dim root = IO.Path.Combine(FileService.LocalDataPath, "frp")
        If Not Directory.Exists(root) Then Exit Sub
        Dim subDirs As String() = Directory.GetDirectories(root)
        For Each folderPath As String In subDirs
            Dim name As String = IO.Path.GetFileName(folderPath)
            If Not name.Equals(currentVersion) Then
                Try
                    Directory.Delete(folderPath, True)
                Catch ex As Exception
                    Log(ex, "[Link] 清理旧版本 frpc 出错")
                End Try
            End If
        Next
    End Sub

    Private Sub EnsureFrpcExecutable(version As String)
        Try
            Dim root = IO.Path.Combine(FileService.LocalDataPath, "frp", version)
            If Not Directory.Exists(root) Then Exit Sub
            Dim target = IO.Path.Combine(root, "StellarFrpc.exe")
            If IO.File.Exists(target) Then Exit Sub
            Dim files = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories)
            Dim best As String = files.FirstOrDefault(Function(p) IO.Path.GetFileName(p).ToLower().Contains("frpc"))
            If String.IsNullOrWhiteSpace(best) AndAlso files.Length > 0 Then best = files(0)
            If Not String.IsNullOrWhiteSpace(best) Then
                Try
                    IO.File.Copy(best, target, True)
                Catch
                End Try
            End If
        Catch ex As Exception
            Log(ex, "[Link] 修正 frpc 可执行文件失败")
        End Try
    End Sub

#End Region

#Region "大厅操作"
    Public Function LobbyPrecheck() As Boolean
        Log("[Link] LobbyPrecheck: IsLobbyAvailable=" & LobbyInfoProvider.IsLobbyAvailable & ", StellarToken=" & If(String.IsNullOrWhiteSpace(Config.Link.StellarToken), "empty", "set"))
        If Not LobbyInfoProvider.IsLobbyAvailable AndAlso String.IsNullOrWhiteSpace(Config.Link.StellarToken) Then
            Hint("大厅功能暂不可用，请稍后再试", HintType.Critical)
            Return False
        End If
        If SelectedProfile IsNot Nothing Then
            If SelectedProfile.Username.Contains("|") Then
                Hint("MC 玩家 ID 不可包含分隔符 (|) ！")
                Return False
            End If
        End If
        If LobbyInfoProvider.RequiresLogin AndAlso String.IsNullOrWhiteSpace(Config.Link.StellarToken) Then
            If String.IsNullOrWhiteSpace(Setup.Get("LinkNaidRefreshToken")) Then
                Hint("请先前往联机设置并登录至 Natayark Network 再进行联机！", HintType.Critical)
                Return False
            End If
            Try
                GetNaidData(Setup.Get("LinkNaidRefreshToken"), True)
            Catch ex As Exception
                Log("[Link] 刷新 Natayark ID 信息失败，需要重新登录")
                Hint("请重新登录 Natayark Network 账号再试！", HintType.Critical)
                Return False
            End Try
            Dim waitCount As Integer = 0
            While String.IsNullOrWhiteSpace(NaidProfile.Username)
                If waitCount > 30 Then Exit While
                Thread.Sleep(500)
                waitCount += 1
            End While
            If String.IsNullOrWhiteSpace(NaidProfile.Username) Then
                Hint("尝试获取 Natayark ID 信息失败", HintType.Critical)
                Return False
            End If
            If LobbyInfoProvider.RequiresRealName AndAlso Not NaidProfile.IsRealNamed Then
                Hint("请先前往 Natayark 账户中心进行实名验证再尝试操作！", HintType.Critical)
                Return False
            End If
            If Not NaidProfile.Status = 0 Then
                Hint("你的 Natayark Network 账号状态异常，可能已被封禁！", HintType.Critical)
                Return False
            End If
        End If
        If String.IsNullOrWhiteSpace(Setup.Get("LinkUsername")) Then
            If Not String.IsNullOrWhiteSpace(Config.Link.StellarToken) Then
                Try
                    Dim ui = StellarFrpApi.GetUserInfo(Config.Link.StellarToken)
                    If ui IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ui.Username) Then
                        Setup.Set("LinkUsername", ui.Username)
                    End If
                Catch ex As Exception
                    Log(ex, "[Link] 获取 StellarFrp 用户名失败")
                End Try
            End If
            If String.IsNullOrWhiteSpace(Setup.Get("LinkUsername")) Then
                Hint("请先前往设置输入一个用户名，或登录至 StellarFrp 再进行联机！", HintType.Critical)
                Return False
            End If
        End If
        If FrpController.Precheck() = 1 Then
            Hint("正在下载联机依赖组件，请稍后...")
            StellarFrpDownloadClient()
            Return False
        End If
        If DlFrpcLoader IsNot Nothing Then
            If DlFrpcLoader.State = LoadState.Loading Then
                Hint("frpc 尚未下载完成，请等待其下载完成后再试！")
                Return False
            ElseIf DlFrpcLoader.State = LoadState.Failed OrElse DlFrpcLoader.State = LoadState.Aborted Then
                Hint("正在下载 frpc，请稍后...")
                StellarFrpDownloadClient()
                Return False
            End If
        End If
        Return True
    End Function
#End Region

End Module
