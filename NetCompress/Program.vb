Imports System.IO
Imports System.IO.Compression
Imports System.Reflection

Public Module Program

    Public Sub Main(ParamArray args As String())

        Try
            UnsafeMain(args)

        Catch ex As Exception
#If DEBUG Then
            Trace.WriteLine(ex.ToString())
#End If
            Console.ForegroundColor = ConsoleColor.Red
            Console.WriteLine("Exception:")
            While ex IsNot Nothing
                Console.WriteLine(ex.Message)
                ex = ex.InnerException
            End While
            Console.ResetColor()

        End Try

    End Sub

    Public Sub UnsafeMain(ParamArray args As String())

        Dim Mode = CompressionMode.Compress
        Dim ShowHelp = False
        Dim MethodName = String.Empty
        Dim BufferSize As Integer?
        Dim AssemblyFile As String = Nothing

        For Each arg In args
            If arg.Equals("-d", StringComparison.OrdinalIgnoreCase) OrElse arg.Equals("/d", StringComparison.OrdinalIgnoreCase) Then
                Mode = CompressionMode.Decompress
            ElseIf arg.StartsWith("-a:", StringComparison.OrdinalIgnoreCase) OrElse arg.StartsWith("/a:", StringComparison.OrdinalIgnoreCase) Then
                AssemblyFile = arg.Substring("-a:".Length)
            ElseIf arg.StartsWith("-m:", StringComparison.OrdinalIgnoreCase) OrElse arg.StartsWith("/m:", StringComparison.OrdinalIgnoreCase) Then
                MethodName = arg.Substring("-m:".Length)
            ElseIf arg.StartsWith("-b:", StringComparison.OrdinalIgnoreCase) OrElse arg.StartsWith("/b:", StringComparison.OrdinalIgnoreCase) Then
                BufferSize = Integer.Parse(arg.Substring("-b:".Length))
            Else
                ShowHelp = True
                Exit For
            End If
        Next

        If ShowHelp Then
            Console.Error.WriteLine($"Syntax:
NetCompress [-d] [-m:GZip|Deflate] [-b:buffersize] [-a:assembly]")
            Return
        End If

        Dim InStream = If(BufferSize.HasValue, Console.OpenStandardInput(BufferSize.Value), Console.OpenStandardInput())
        Dim OutStream = If(BufferSize.HasValue, Console.OpenStandardOutput(BufferSize.Value), Console.OpenStandardOutput())

        If Not String.IsNullOrEmpty(MethodName) Then

            Dim AssemblyForType As Assembly
            If String.IsNullOrEmpty(AssemblyFile) Then
                AssemblyForType = GetType(GZipStream).Assembly
            Else
                AssemblyForType = Assembly.Load(AssemblyName.GetAssemblyName(AssemblyFile))
            End If

            Dim typeName = $"System.IO.Compression.{MethodName}Stream"

            Dim MethodType = AssemblyForType.GetType(typeName, throwOnError:=False, ignoreCase:=True)

            If MethodType Is Nothing OrElse
                MethodType.IsAbstract OrElse
                Not GetType(Stream).IsAssignableFrom(MethodType) Then

                Console.Error.WriteLine($"Class {typeName} not found in '{AssemblyForType.FullName}'.")
                Return
            End If

            If Mode = CompressionMode.Compress Then

                OutStream = DirectCast(Activator.CreateInstance(MethodType, {OutStream, Mode}), Stream)

            ElseIf Mode = CompressionMode.Decompress Then

                InStream = DirectCast(Activator.CreateInstance(MethodType, {InStream, Mode}), Stream)

            End If

        End If

        Using InStream

            Using OutStream

                If BufferSize.HasValue Then
                    InStream.CopyTo(OutStream, BufferSize.Value)
                Else
                    InStream.CopyTo(OutStream)
                End If

            End Using

        End Using

    End Sub

End Module
