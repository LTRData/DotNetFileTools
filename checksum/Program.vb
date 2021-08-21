Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

Public Module Program

    Public Sub Main(args As String())

        Try
            SafeMain(args)

        Catch ex As Exception
#If DEBUG Then
            Trace.WriteLine(ex.ToString())
#End If
            Console.WriteLine("Exception:")
            Console.WriteLine(ex.Message)

        End Try

    End Sub

    Sub SafeMain(args As String())

        If args.Length = 0 OrElse Array.Find(args, Function(arg) arg = "/?") IsNot Nothing Then
            Console.WriteLine("Generic .NET checksum calculation tool.")
            Console.WriteLine("Copyright (c) 2012-2016, Olof Lagerkvist, LTR Data.")
            Console.WriteLine("http://www.ltr-data.se/opencode.html")
            Console.WriteLine()
            Console.WriteLine("Syntax:")
            Console.WriteLine()
            Console.WriteLine("checksum [/X:assembly] [/S] [/A:algorithm] [/K:key] file1")
            Console.WriteLine("        [[/A:algorithm] [/K:key] file2 ...]")
            Console.WriteLine()
            Console.WriteLine("checksum [/X:assembly] /L")
            Console.WriteLine()
            Console.WriteLine("/X      Specify name and path to assembly file to search for hash algorithms.")
            Console.WriteLine()
            Console.WriteLine("/S      Search subdirectories.")
            Console.WriteLine()
            Console.WriteLine("/A      Specifies algorithm. Can be any .NET supported hashing algorithm, such")
            Console.WriteLine("        as MD5, SHA1 or RIPEMD160.")
            Console.WriteLine()
            Console.WriteLine("/K      For HMAC shared-key hash providers, specifies secret key for checksum.")
            Console.WriteLine()
            Console.WriteLine("/L      Lists available hash algorithms.")
            Console.WriteLine()
            Return
        End If

        Dim alg = "md5"
        Dim key As String = Nothing
        Dim search_option = SearchOption.TopDirectoryOnly
        Dim value = False

        Dim threads As New List(Of Thread)

        For Each arg In args

            If arg.Equals("/L", StringComparison.InvariantCultureIgnoreCase) Then
                ListHashProviders()
            ElseIf arg.StartsWith("/X:", StringComparison.InvariantCultureIgnoreCase) Then
                Dim asmfile = arg.Substring("/X:".Length)
                Dim asmname = AssemblyName.GetAssemblyName(asmfile)
                Assembly.Load(asmname)
            ElseIf arg.StartsWith("/A:", StringComparison.InvariantCultureIgnoreCase) Then
                alg = arg.Substring("/A:".Length)
            ElseIf arg.StartsWith("/K:", StringComparison.InvariantCultureIgnoreCase) Then
                key = arg.Substring("/K:".Length)
            ElseIf arg.Equals("/S", StringComparison.InvariantCultureIgnoreCase) Then
                search_option = SearchOption.AllDirectories
            ElseIf arg.StartsWith("/V:", StringComparison.InvariantCultureIgnoreCase) Then
                Dim valuestr = Encoding.UTF8.GetBytes(arg.Substring("/V:".Length))
                PrintCheckSumForData(alg, key, valuestr)
            ElseIf arg.Equals("/V", StringComparison.InvariantCultureIgnoreCase) Then
                value = True
            ElseIf value Then
                Dim valuestr = Encoding.UTF8.GetBytes(arg)
                PrintCheckSumForData(alg, key, valuestr)
            Else
                Dim thread = PrintCheckSumForFilesThread(alg, key, arg, search_option)
                threads.Add(thread)
            End If

        Next

        threads.ForEach(Sub(thread) thread.Join())

        If Debugger.IsAttached Then
            Console.ReadKey()
        End If

    End Sub

    Private Function PrintCheckSumForFilesThread(alg As String, key As String, arg As String, search_option As SearchOption) As Thread
        Dim thread As New Thread(Sub() PrintCheckSumForFiles(alg, key, arg, search_option))
        thread.Start()
        Return thread
    End Function

    ReadOnly providers As New Dictionary(Of String, HashAlgorithm)(StringComparer.InvariantCultureIgnoreCase)

    Sub ListHashProviders()

        Dim List As New List(Of String)

        For Each Assembly In AppDomain.CurrentDomain.GetAssemblies()
            For Each Type In Assembly.GetTypes()
                If Type.IsClass AndAlso
                  Not Type.IsAbstract AndAlso
                  GetType(HashAlgorithm).IsAssignableFrom(Type) AndAlso
                  Type.GetConstructor(Type.EmptyTypes) IsNot Nothing Then

                    Dim name = Type.Name
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

    Function GetHashProvider(alg As String) As HashAlgorithm

        Dim algorithm As HashAlgorithm = Nothing

        If Not providers.TryGetValue(alg, algorithm) Then

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
                              Type.Name.Equals(alg & "CryptoServiceProvider", StringComparison.OrdinalIgnoreCase) OrElse
                              Type.Name.Equals(alg & "Managed", StringComparison.OrdinalIgnoreCase) OrElse
                              Type.Name.Equals(alg & "Cng", StringComparison.OrdinalIgnoreCase)) Then

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

            providers.Add(alg, algorithm)

        End If

        Return algorithm

    End Function

    Sub PrintCheckSumForFiles(alg As String, key As String, filename_pattern As String, search_option As SearchOption)

        Try
            If String.IsNullOrEmpty(filename_pattern) OrElse
                "-".Equals(filename_pattern, StringComparison.Ordinal) Then

                PrintCheckSumForFile(alg, key, "-")

            Else

                Dim dir = Path.GetDirectoryName(filename_pattern)
                Dim filepart = Path.GetFileName(filename_pattern)

                If String.IsNullOrEmpty(dir) Then
                    dir = "."
                End If

                For Each filename In Directory.GetFiles(dir, filepart, search_option)
                    If filename.StartsWith(".\", StringComparison.Ordinal) Then
                        filename = filename.Substring(2)
                    End If
                    PrintCheckSumForFile(alg, key, filename)
                Next

            End If

        Catch ex As Exception
            Console.WriteLine(filename_pattern & ": " & ex.Message)

        End Try

    End Sub

    Sub PrintCheckSumForFile(alg As String, key As String, filename As String)

        Dim algorithm = GetHashProvider(alg)

        If algorithm Is Nothing Then
            Console.WriteLine("Hash algorithm '" & alg & "' not supported.")
            Return
        End If

        If TypeOf algorithm Is KeyedHashAlgorithm Then
            If String.IsNullOrEmpty(key) Then
                Console.WriteLine("Hash algorithm '" & alg & "' requires key.")
                Return
            End If
            DirectCast(algorithm, KeyedHashAlgorithm).Key = Encoding.UTF8.GetBytes(key)
        ElseIf Not String.IsNullOrEmpty(key) Then
            Console.WriteLine("Hash algorithm '" & alg & "' does not support keyed hashing.")
            Return
        End If

        Dim buffersize = algorithm.HashSize * 8192

        Dim hash As Byte()

        Dim fs As Stream
        If "-".Equals(filename, StringComparison.Ordinal) Then
            fs = Console.OpenStandardInput(buffersize)
        Else
            Try
                fs = New FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read Or FileShare.Delete, buffersize, FileOptions.SequentialScan)

            Catch ex As Exception
                Console.WriteLine("Error opening file '" & filename & "': " & ex.Message)
                Return

            End Try
        End If

        Using fs
            hash = algorithm.ComputeHash(fs)
        End Using

        Dim sb As New StringBuilder
        Array.ForEach(hash, Sub(b) sb.Append(b.ToString("x2")))

        Console.WriteLine(sb.ToString() & " *" & filename)

    End Sub

    Sub PrintCheckSumForData(alg As String, key As String, data As Byte())

        Dim algorithm = GetHashProvider(alg)

        If algorithm Is Nothing Then
            Console.WriteLine("Hash algorithm '" & alg & "' not supported.")
            Return
        End If

        If TypeOf algorithm Is KeyedHashAlgorithm Then
            If String.IsNullOrEmpty(key) Then
                Console.WriteLine("Hash algorithm '" & alg & "' requires key.")
                Return
            End If
            DirectCast(algorithm, KeyedHashAlgorithm).Key = Encoding.UTF8.GetBytes(key)
        ElseIf Not String.IsNullOrEmpty(key) Then
            Console.WriteLine("Hash algorithm '" & alg & "' does not support keyed hashing.")
            Return
        End If

        Dim buffersize = algorithm.HashSize * 8192

        Dim hash As Byte()

        hash = algorithm.ComputeHash(data)

        Dim sb As New StringBuilder(hash.Length << 1)

        Array.ForEach(hash, Sub(b) sb.Append(b.ToString("x2")))

        sb.Append(" *")

        Console.WriteLine(sb.ToString())

    End Sub

End Module
