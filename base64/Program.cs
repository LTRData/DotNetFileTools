using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace base64
{
    public static class Program
    {
        public static void Main(params string[] args)
        {
            try
            {
                UnsafeMain(args);
            }
            catch (Exception ex)
            {
#if DEBUG
                Trace.WriteLine(ex.ToString());
#endif
                Console.Error.WriteLine(ex.GetBaseException().Message);
            }
        }

        public static void UnsafeMain(params string[] args)
        {
            if (args == null || args.Length == 0 || args[0].Equals("-e", StringComparison.Ordinal))
            {
                if (args == null || args.Length <= 1)
                {
                    var buffer = new MemoryStream();
                    Console.OpenStandardInput().CopyTo(buffer);
                    Console.WriteLine(Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length, Base64FormattingOptions.None));
                }
                else 
                {
                    for (var i = 1; i < args.Length; i++)
                    {
                        var buffer = Encoding.UTF8.GetBytes(args[i]);
                        Console.WriteLine(Convert.ToBase64String(buffer));
                    }
                }
            }
            else if (args[0].Equals("-d", StringComparison.Ordinal))
            {
                if (args.Length <= 1)
                {
                    var buffer = Convert.FromBase64String(Console.In.ReadToEnd());
                    Console.OpenStandardOutput().Write(buffer, 0, buffer.Length);
                }
                else
                {
                    for (var i = 1; i < args.Length; i++)
                    {
                        var buffer = Convert.FromBase64String(args[i]);
                        Console.OpenStandardOutput().Write(buffer, 0, buffer.Length);
                    }
                }
            }
            else
            {
                Console.WriteLine("Syntax: base64 -d|-e [files ...]");
            }
        }
    }
}
