Public Class Program

    Public Shared Sub Main(ParamArray args As String())

        Dim PG = False
        If args.Length > 0 Then
            If args(0).Equals("/PG", StringComparison.InvariantCultureIgnoreCase) Then
                PG = True
            Else
                Console.WriteLine("Appends Luhn checksum character to input strings.")
                Console.WriteLine()
                Console.WriteLine("Syntax:")
                Console.WriteLine("luhn [/PG]")
                Console.WriteLine()
                Console.WriteLine("/PG      Create PlusGirot counted OCR string with")
                Console.WriteLine("         both counter and Luhn checksum characters.")
                Console.WriteLine("         Without this parameter, only Luhn checksum")
                Console.WriteLine("         character is appended.")
                Return
            End If
        End If

        Do
            Dim Line = Console.ReadLine()
            If String.IsNullOrEmpty(Line) Then
                Exit Do
            End If

            Console.WriteLine(AppendLuhn(Line, PG))
        Loop

    End Sub

    ''' <summary>
    ''' Calculate Luhn checksum character for numeric string. Non-numeric characters are ignored.
    ''' </summary>
    ''' <param name="Str">String containing numeric characters.</param>
    ''' <returns>Numeric characters in input string with calculated checksum character appended.</returns>
    <DebuggerHidden()>
    Public Shared Function AppendLuhn(Str As String, PG As Boolean) As String
        Dim Digits As New List(Of Char)(Str)
        Digits.RemoveAll(Function(Chr) Not Char.IsDigit(Chr))
        Str = Digits.ToArray()
        If PG Then
            Str &= (Str.Length + 2) Mod 10
        End If

        Return Str & CalculateLuhn(Str)
    End Function

    ''' <summary>
    ''' Calculate and returns Luhn checksum character for numeric string.
    ''' </summary>
    ''' <param name="Str">String containing numeric characters. An exception will occur if string contains non-numeric characters.</param>
    ''' <returns>Calculated Luhn checksum character.</returns>
    <DebuggerHidden()>
    Public Shared Function CalculateLuhn(Str As String) As Char
        Dim Checksum As Integer
        For i = Str.Length - 1 To 0 Step -1
            For Each c In (CInt(Str(i).ToString()) * (1 + ((Str.Length - i) And 1))).ToString()
                Checksum += CInt(c.ToString())
            Next
        Next
        Checksum = (10 - (Checksum Mod 10)) Mod 10
        Return Checksum.ToString()(0)
    End Function

End Class

