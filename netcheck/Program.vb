Imports System.IO
Imports System.Reflection
#If NET462_OR_GREATER OrElse NETSTANDARD OrElse NETCOREAPP Then
Imports System.Reflection.Metadata
Imports System.Reflection.PortableExecutable
#End If
Imports System.Runtime.Versioning

Public Module Program

    Private WithEvents CurrentAppDomain As AppDomain = AppDomain.CurrentDomain

    Public Function Main(ParamArray args As String()) As Integer

        Dim errors = 0

        Dim nodep = False

        For Each arg In args

            If arg.Equals("-l", StringComparison.Ordinal) Then
                nodep = True
                Continue For
            End If

            Try

                arg = Path.GetFullPath(arg)

                Dim asmname = AssemblyName.GetAssemblyName(arg)

                DisplayDependencies(New List(Of AssemblyName), Path.GetDirectoryName(arg), asmname, 0, nodep)

            Catch ex As Exception
                Console.ForegroundColor = ConsoleColor.Red
                Console.Error.Write(ex.ToString())
                Console.Error.Write(": ")
                Console.Error.WriteLine(arg)
                Console.ResetColor()

                errors += 1

            End Try

        Next

        Return errors

    End Function

    Sub DisplayDependencies(asmlist As List(Of AssemblyName), basepath As String, asmname As AssemblyName, indentlevel As Integer, nodep As Boolean)

        If asmlist.Find(Function(name) AssemblyName.ReferenceMatchesDefinition(name, asmname)) IsNot Nothing Then
            Return
        End If

        asmlist.Add(asmname)

        Dim asm As Assembly
        Try
            If asmname.CodeBase IsNot Nothing Then
                asm = Assembly.LoadFrom(asmname.CodeBase)
            Else
                asm = Assembly.Load(asmname)
            End If

        Catch
            Try
                asm = Assembly.LoadFrom(Path.Combine(basepath, $"{asmname.Name}.dll"))
                asmname = asm.GetName()

            Catch ex As Exception
                Console.ForegroundColor = ConsoleColor.Yellow
                Console.Error.WriteLine($"Error loading {asmname}: {ex.GetBaseException().Message}")
                Console.ResetColor()
                Return

            End Try

        End Try

        Console.Write(New String(" "c, 2 * indentlevel))
        If asm.GlobalAssemblyCache Then
            Console.ForegroundColor = ConsoleColor.Green
        Else
            Console.ForegroundColor = ConsoleColor.White
            Console.Write($"{asm.Location}: ")
        End If
        Console.Write(asmname.FullName)

#If NET462_OR_GREATER OrElse NETSTANDARD OrElse NETCOREAPP Then
        Using reader As New PEReader(asm.GetFiles()(0))

            Dim metadataversion = reader.GetMetadataReader().MetadataVersion

            Dim target_framework = asm.GetCustomAttributes(Of TargetFrameworkAttribute)().FirstOrDefault()?.FrameworkName

            Dim framework = GetTargetFramework(metadataversion, target_framework)

            If Not String.IsNullOrWhiteSpace(framework) Then
                Console.Write(", ")
                Console.Write(framework)
            End If

        End Using

#End If

        Console.ResetColor()
        Console.WriteLine()

        If Not nodep Then

            For Each refasm In asm.GetReferencedAssemblies()

                DisplayDependencies(asmlist, basepath, refasm, indentlevel + 1, nodep)

            Next

        End If

    End Sub

#If NET462_OR_GREATER OrElse NETSTANDARD OrElse NETCOREAPP Then
    Private Function GetTargetFramework(metadataversion As String, target_framework As String) As String

        If String.IsNullOrWhiteSpace(metadataversion) Then
            Return Nothing
        End If

        If String.IsNullOrWhiteSpace(target_framework) Then

            Dim netfx = metadataversion.Split({"v"c, "."c})

            Return $"net{netfx(1)}{netfx(2)}"

        End If

        If target_framework.StartsWith(".NETFramework,Version=v", StringComparison.Ordinal) Then

            Return $"net{target_framework.Substring(".NETFramework,Version=v".Length).Replace(".", "")}"

        End If

        Dim sep = target_framework.IndexOf(",Version=v", StringComparison.Ordinal)

        Dim fx = target_framework.Remove(sep).TrimStart("."c).ToLowerInvariant()
        Dim ver = target_framework.Substring(sep + ",Version=v".Length)

        If fx.Equals("netcoreapp", StringComparison.Ordinal) AndAlso ver(0) >= "5"c Then
            fx = "net"
        End If

        Return $"{fx}{ver}"

    End Function
#End If

    Private Sub CurrentAppDomain_UnhandledException(sender As Object, e As UnhandledExceptionEventArgs) Handles CurrentAppDomain.UnhandledException

        Console.ForegroundColor = ConsoleColor.Red
        Console.Error.WriteLine($"Exception: {e.ExceptionObject}")

    End Sub
End Module
