Imports System
Imports System.Threading.Tasks
Imports PWFAuth

Namespace VbFrameworkTest
    Module Program
        Private Pass As Integer = 0
        Private Fail As Integer = 0

        Sub Check(name As String, ok As Boolean, Optional detail As String = "")
            If ok Then
                Pass += 1
                Console.WriteLine("  ok  " & name)
            Else
                Fail += 1
                Console.WriteLine("FAIL  " & name & If(detail <> "", "  -- " & detail, ""))
            End If
        End Sub

        Sub Main()
            Console.WriteLine("Runtime: " & System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)

            ' A test application's credentials — never committed; set them in the environment.
            Dim secret As String = Environment.GetEnvironmentVariable("PWFAUTH_APP_SECRET")
            Dim key As String = Environment.GetEnvironmentVariable("PWFAUTH_TEST_KEY")
            If String.IsNullOrWhiteSpace(secret) OrElse String.IsNullOrWhiteSpace(key) Then
                Console.Error.WriteLine("Set PWFAUTH_APP_SECRET (your test app's secret) and PWFAUTH_TEST_KEY (a license key of that app), then run again:")
                Console.Error.WriteLine("  PowerShell:  $env:PWFAUTH_APP_SECRET = ""...""; $env:PWFAUTH_TEST_KEY = ""XXXXX-XXXXX-XXXXX-XXXXX""")
                Console.Error.WriteLine("  cmd:         set PWFAUTH_APP_SECRET=...  and  set PWFAUTH_TEST_KEY=XXXXX-XXXXX-XXXXX-XXXXX")
                Environment.Exit(2)
                Return
            End If

            MainAsync(secret.Trim(), key.Trim()).GetAwaiter().GetResult()
            Console.WriteLine()
            Console.WriteLine("SUMMARY: " & Pass & " passed, " & Fail & " failed")
            Environment.Exit(If(Fail > 0, 1, 0))
        End Sub

        Async Function MainAsync(secret As String, key As String) As Task
            Console.WriteLine("HardwareId: " & HardwareId.Get())

            Using client As New PwfClient(secret)
                ' VB event syntax must work — this is the main reason for CLS compliance.
                AddHandler client.SessionEnded, Sub(s, e)
                                                    Console.WriteLine("   [event] " & e.ErrorCode & " - " & e.Message)
                                                End Sub

                Dim login = Await client.LoginAsync(key)
                Check("LoginAsync from VB.NET on .NET Framework", login.Success, login.ToString())
                Check("IsSignedIn", client.IsSignedIn)

                Dim hb = Await client.HeartbeatAsync()
                Check("HeartbeatAsync", hb.Success, hb.ToString())

                Dim info = Await client.GetAppInfoAsync()
                Check("GetAppInfoAsync (envelope-GET)", info.Success, info.ToString())

                Dim texts = Await client.GetTextsAsync()
                Dim map = texts.GetStringMap("texts")
                Check("GetTextsAsync + GetStringMap",
                      map.ContainsKey("welcome_message") AndAlso map("welcome_message") = "Welcome from the NuGet package")

                Dim upd = Await client.CheckUpdateAsync("1.0.0")
                Check("CheckUpdateAsync", upd.Success, upd.ToString())

                Dim opts As New PwfClientOptions()
                opts.AppSecret = secret
                opts.MaxHeartbeatFailures = 2
                Check("PwfClientOptions settable from VB", opts.MaxHeartbeatFailures = 2)

                Check("PwfErrorCodes.EndsSession(MAINTENANCE)", PwfErrorCodes.EndsSession(PwfErrorCodes.Maintenance))
                Check("PwfErrorCodes.EndsSession(INVALID_KEY) is False", Not PwfErrorCodes.EndsSession(PwfErrorCodes.InvalidKey))

                Dim logout = Await client.LogoutAsync()
                Check("LogoutAsync", logout IsNot Nothing AndAlso logout.Success)
            End Using
        End Function
    End Module
End Namespace
