Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports LTRLib.LTRGeneric

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

    Public Sub UnsafeMain(ParamArray cmdLine As String())

        Dim cmd = StringSupport.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase)

        Dim alg = "md5"
        Dim key As String = Nothing
        Dim search_option = SearchOption.TopDirectoryOnly
        Dim output_code = False

        Dim threads As New List(Of Thread)

        For Each arg In cmd

            If arg.Key.Equals("x", StringComparison.OrdinalIgnoreCase) Then
                For Each asmfile In arg.Value
                    Dim asmname = AssemblyName.GetAssemblyName(asmfile)
                    Assembly.Load(asmname)
                Next
            ElseIf arg.Key.Equals("l", StringComparison.OrdinalIgnoreCase) Then
                ListHashProviders()
            ElseIf arg.Key.Equals("a", StringComparison.OrdinalIgnoreCase) Then
                alg = arg.Value.Single()
            ElseIf arg.Key.Equals("k", StringComparison.OrdinalIgnoreCase) Then
                key = arg.Value.SingleOrDefault()
            ElseIf arg.Key.Equals("s", StringComparison.OrdinalIgnoreCase) Then
                search_option = SearchOption.AllDirectories
            ElseIf arg.Key.Equals("c", StringComparison.OrdinalIgnoreCase) Then
                output_code = True
            ElseIf arg.Key.Equals("v", StringComparison.OrdinalIgnoreCase) Then
                For Each valuestr In arg.Value.Concat(cmd(""))
                    Dim valuebytes = Encoding.UTF8.GetBytes(valuestr)
                    PrintCheckSumForData(alg, key, output_code, valuebytes)
                Next
            ElseIf arg.Key.Equals("", StringComparison.Ordinal) Then
                For Each file In arg.Value
                    Dim thread = PrintCheckSumForFilesThread(alg, key, file, output_code, search_option)
                    threads.Add(thread)
                Next
            Else

                Console.WriteLine("Generic .NET checksum calculation tool.
Copyright (c) 2012-2022, Olof Lagerkvist, LTR Data.
http://ltr-data.se/opencode.html

Syntax for calculating hash of file data:
checksum [-x:assembly] [-s] [-a:algorithm] [-k:key] file1 [file2 ...]

Syntax for calculating hash of UTF8 bytes of a string:
checksum [-x:assembly] [-s] [-a:algorithm] [-k:key] -v:string

List available hash algorithms:
checksum [-x:assembly] -l

-x      Specify name and path to assembly file to search for hash algorithms.

-s      Search subdirectories for files to hash.

-a      Specifies algorithm. Can be any .NET supported hashing algorithm, such
        as MD5, SHA1 or RIPEMD160.

-k      For HMAC shared-key hash providers, specifies secret key for checksum.

-c      Output in C/C++/C# code format.

-l      Lists available hash algorithms.
")

                Return

            End If

        Next

        threads.ForEach(Sub(thread) thread.Join())

        If Debugger.IsAttached Then
            Console.ReadKey()
        End If

    End Sub

    Private Function PrintCheckSumForFilesThread(alg As String, key As String, arg As String, output_code As Boolean, search_option As SearchOption) As Thread
        Dim thread As New Thread(Sub() PrintCheckSumForFiles(alg, key, arg, output_code, search_option))
        thread.Start()
        Return thread
    End Function

    Private ReadOnly _providers As New Dictionary(Of String, HashAlgorithm)(StringComparer.OrdinalIgnoreCase)

    Public Sub ListHashProviders()

        Dim assemblies = AppDomain.CurrentDomain.GetAssemblies()

#If NETCOREAPP Then
        If Array.IndexOf(assemblies, GetType(SHA256).Assembly) < 0 Then
            assemblies = AppDomain.CurrentDomain.GetAssemblies()
        End If
        If Array.IndexOf(assemblies, GetType(MD5).Assembly) < 0 Then
            assemblies = AppDomain.CurrentDomain.GetAssemblies()
        End If
        If Array.IndexOf(assemblies, GetType(TripleDES).Assembly) < 0 Then
            assemblies = AppDomain.CurrentDomain.GetAssemblies()
        End If
#End If

        Dim List As New List(Of String)

        For Each Assembly In assemblies
            For Each Type In Assembly.GetTypes()
                If Type.IsClass AndAlso
                  Not Type.IsAbstract AndAlso
                  GetType(HashAlgorithm).IsAssignableFrom(Type) AndAlso
                  Type.GetConstructor(Type.EmptyTypes) IsNot Nothing Then

                    Dim name = Type.Name
                    If name.Equals("Implementation", StringComparison.Ordinal) AndAlso
                        Type.DeclaringType IsNot Nothing Then

                        name = Type.DeclaringType.Name
                    End If
                    For Each suffix In {"CryptoServiceProvider", "Managed", "Cng"}
                        If name.EndsWith(suffix) Then
                            name = name.Remove(name.Length - suffix.Length)
                            Exit For
                        End If
                    Next
                    If Not List.Contains(name) Then
                        List.Add(name)
                    End If
                End If
            Next
        Next

        List.ForEach(AddressOf Console.WriteLine)

    End Sub

    Public Function GetHashProvider(alg As String) As HashAlgorithm

        Dim algorithm As HashAlgorithm = Nothing

        SyncLock _providers

            If Not _providers.TryGetValue(alg, algorithm) Then

                algorithm = HashAlgorithm.Create(alg)

                If algorithm Is Nothing Then

                    Dim algType As Type = Nothing

                    For Each Assembly In AppDomain.CurrentDomain.GetAssemblies()
                        For Each Type In Assembly.GetTypes()
                            If Type.IsClass AndAlso
                              Not Type.IsAbstract AndAlso
                              GetType(HashAlgorithm).IsAssignableFrom(Type) AndAlso
                              Type.GetConstructor(Type.EmptyTypes) IsNot Nothing AndAlso
                              (Type.Name.Equals(alg, StringComparison.OrdinalIgnoreCase) OrElse
                                  Type.Name.Equals($"{alg}CryptoServiceProvider", StringComparison.OrdinalIgnoreCase) OrElse
                                  Type.Name.Equals($"{alg}Managed", StringComparison.OrdinalIgnoreCase) OrElse
                                  Type.Name.Equals($"{alg}Cng", StringComparison.OrdinalIgnoreCase)) Then

                                algType = Type
                                Exit For

                            End If
                        Next

                        If algType IsNot Nothing Then
                            Exit For
                        End If
                    Next

                    If algType Is Nothing Then
                        Return Nothing
                    End If

                    algorithm = DirectCast(Activator.CreateInstance(algType), HashAlgorithm)

                End If

                _providers.Add(alg, algorithm)

            End If

        End SyncLock

        Return algorithm

    End Function

    Public Sub PrintCheckSumForFiles(alg As String, key As String, filename_pattern As String, output_code As Boolean, search_option As SearchOption)

        Try
            If String.IsNullOrEmpty(filename_pattern) OrElse
                "-".Equals(filename_pattern, StringComparison.Ordinal) Then

                PrintCheckSumForFile(alg, key, "-", output_code)

            Else

                Dim dir = Path.GetDirectoryName(filename_pattern)
                Dim filepart = Path.GetFileName(filename_pattern)

                If String.IsNullOrEmpty(dir) Then
                    dir = "."
                End If

                Dim found = False

                For Each filename In Directory.GetFiles(dir, filepart, search_option)
                    found = True
                    If filename.StartsWith(".\", StringComparison.Ordinal) Then
                        filename = filename.Substring(2)
                    End If
                    PrintCheckSumForFile(alg, key, filename, output_code)
                Next

                If Not found Then
                    Console.Error.WriteLine($"File '{filename_pattern}' not found")
                End If

            End If

        Catch ex As Exception
            Console.ForegroundColor = ConsoleColor.Red
            Console.Error.WriteLine($"{filename_pattern}: {ex.Message}")
            Console.ResetColor()

        End Try

    End Sub

    Public Sub PrintCheckSumForFile(alg As String, key As String, filename As String, output_code As Boolean)

        Dim algorithm = GetHashProvider(alg)

        If algorithm Is Nothing Then
            Console.WriteLine($"Hash algorithm '{alg}' not supported.")
            Return
        End If

        If TypeOf algorithm Is KeyedHashAlgorithm Then
            If String.IsNullOrEmpty(key) Then
                Console.WriteLine($"Hash algorithm '{alg}' requires key.")
                Return
            End If
            DirectCast(algorithm, KeyedHashAlgorithm).Key = Encoding.UTF8.GetBytes(key)
        ElseIf Not String.IsNullOrEmpty(key) Then
            Console.WriteLine($"Hash algorithm '{alg}' does not support keyed hashing.")
            Return
        End If

        Dim buffersize = algorithm.HashSize * 8192

        Dim hash As Byte()

        Try
            Using fs = OpenFile(filename, buffersize)
                hash = algorithm.ComputeHash(fs)
            End Using

        Catch ex As Exception
            Console.ForegroundColor = ConsoleColor.Red
            Console.Error.WriteLine($"Error opening or reading file '{filename}': {ex.Message}")
            Console.ResetColor()
            Return

        End Try

        PrintChecksum(hash, filename, output_code)

    End Sub

    Private Function OpenFile(filename As String, buffersize As Integer) As Stream

        If "-".Equals(filename, StringComparison.Ordinal) Then
            Return Console.OpenStandardInput(buffersize)
        Else
            Return New FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read Or FileShare.Delete, buffersize, FileOptions.SequentialScan)
        End If

    End Function

    Public Sub PrintCheckSumForData(alg As String, key As String, output_code As Boolean, data As Byte())

        Dim algorithm = GetHashProvider(alg)

        If algorithm Is Nothing Then
            Console.WriteLine($"Hash algorithm '{alg}' not supported.")
            Return
        End If

        If TypeOf algorithm Is KeyedHashAlgorithm Then
            If String.IsNullOrEmpty(key) Then
                Console.WriteLine($"Hash algorithm '{alg}' requires key.")
                Return
            End If
            DirectCast(algorithm, KeyedHashAlgorithm).Key = Encoding.UTF8.GetBytes(key)
        ElseIf Not String.IsNullOrEmpty(key) Then
            Console.WriteLine($"Hash algorithm '{alg}' does not support keyed hashing.")
            Return
        End If

        Dim buffersize = algorithm.HashSize * 8192

        Dim hash As Byte()

        hash = algorithm.ComputeHash(data)

        PrintChecksum(hash, "", output_code)

    End Sub

    Public Sub PrintChecksum(hash As Byte(), filename As String, output_code As Boolean)

        Dim sb As StringBuilder

        If output_code Then

            sb = New StringBuilder(hash.Length * 6 + 10 + filename.Length)

            sb.Append("{ ")

            sb.Append(String.Join(", ", Array.ConvertAll(hash, Function(b) $"0x{b:x2}")))

            sb.Append(" };  // ").Append(filename)

        Else

            sb = New StringBuilder(hash.Length * 2 + 2 + filename.Length)

            Array.ForEach(hash, Sub(b) sb.Append(b.ToString("x2")))

            sb.Append(" *").Append(filename)

        End If

        Console.WriteLine(sb.ToString())

    End Sub

End Module
