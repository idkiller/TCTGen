using System;
using System.Reflection;
using System.IO;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace apiviewer
{
    [AttributeUsage(AttributeTargets.Property)]
    class PrintableAttribute : Attribute
    {
        public PrintableAttribute(int order)
        {
            Order = order;
        }

        public int Order { get; private set; }
    }

    enum Category
    {
        None, Type, Property, StaticField, Method, Field
    }

    class API
    {
        public static API Empty = new API();

        public API Parent {get; set;}

        public string DeclaredType {get; set;}
        [Printable(2)]
        public Category Category {get; set;}
        [Printable(4)]
        public string Name {get; set;}
        [Printable(3)]
        public string Type {get; set;}
        [Printable(5)]
        public string Tested {get; set;} 

        public API (API parent, Category c, string name, string type)
        {
            Parent = parent;
            DeclaredType = parent?.Name;
            Category = c;
            Name = name;
            Type = type;
        }

        public API (API parent, Category c, string name) : this(parent, c, name, null)
        {
        }

        public API (Category c, string name) : this(Empty, c, name, null)
        {
        }

        public API () : this(null, apiviewer.Category.None, null)
        {
        }
    }

	class Program
	{
        static List<API> apis = new List<API>();

		static void Main(string[] args)
		{
			if (args.Length < 1) return;

			string cwd = Directory.GetCurrentDirectory();
			string path = Path.Combine(cwd, args[0]);

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += MyResolveEventHandler;

			try
			{
                var assembly = Assembly.LoadFile(path);
                var types = assembly.GetTypes().Where(t => 
                        !t.IsDefined(typeof(CompilerGeneratedAttribute), false) &&
                        !t.IsEnum &&
                        !t.IsInterface &&
                        !t.IsSubclassOf(typeof(Delegate)));

                foreach (Type type in types)
                {
                    string typeName = type.Name;
                    var t = new API(Category.Type, typeName);
                    apis.Add(t);

                    var staticFieldsInfo = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    foreach (var field in staticFieldsInfo)
                    {
                        apis.Add(new API(t, Category.StaticField, field.Name, field.FieldType.Name));
                    }

                    var propertyInfo = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    foreach (var property in propertyInfo)
                    {
                        apis.Add(new API(t, Category.Property, property.Name, property.PropertyType.Name));
                    }

                    var methodInfo = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => {
                            var method = m as MethodBase;
                            return method == null || !method.IsSpecialName;
                            });
                    foreach (var method in methodInfo)
                    {
                        var pars = method.GetParameters();
                        var psb = new StringBuilder();
                        psb.Append(' ');
                        foreach (var p in pars)
                        {
                            psb.Append(p.ParameterType.Name);
                            psb.Append(' ');
                            psb.Append(p.Name);
                            psb.Append(' ');
                        }

                        apis.Add(new API(t, Category.Method, $"{method.Name}({psb.ToString()})", method.ReturnType.Name));
                    }

                    var fieldsInfo = type.GetFields(BindingFlags.Public | BindingFlags.DeclaredOnly);
                    foreach (var field in fieldsInfo)
                    {
                        apis.Add(new API(t, Category.Field, field.Name, field.FieldType.Name));
                    }
                }
			}
			catch (ReflectionTypeLoadException ex)
			{
				StringBuilder sb = new StringBuilder();
				foreach (Exception exSub in ex.LoaderExceptions)
				{
					sb.AppendLine(exSub.Message);
					FileNotFoundException exFileNotFound = exSub as FileNotFoundException;
					if (exFileNotFound != null)
					{                
						if(!string.IsNullOrEmpty(exFileNotFound.FusionLog))
						{
							sb.AppendLine("Fusion Log:");
							sb.AppendLine(exFileNotFound.FusionLog);
						}
					}
					sb.AppendLine();
				}
				string errorMessage = sb.ToString();
                Console.WriteLine(errorMessage);
			}

            PrintMarkdownTable();
		}

        static Assembly FindDll(string dir, string fullName)
        {
            var files = Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var asm = Assembly.LoadFile(file);
                if (fullName == asm.FullName)
                {
                    return asm;
                }
            }

            return null;
        }

        static Assembly MyResolveEventHandler(object sender, ResolveEventArgs args)
        {
            var cwd = System.Environment.CurrentDirectory;
            return FindDll(cwd, args.Name);
        }

        static void PrintMarkdownTable()
        {
            var apiProperty = typeof(API).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => Attribute.IsDefined(p, typeof(PrintableAttribute)))
                .OrderBy(p => (p?.GetCustomAttribute(typeof(PrintableAttribute), false) as PrintableAttribute)?.Order);
            Dictionary<string, int> maxSize = new Dictionary<string, int>();
            foreach (var property in apiProperty)
            {
                maxSize[property.Name] = property.Name.Length;
            }

            foreach (var api in apis)
            {
                foreach (var property in apiProperty)
                {
                    object v = property.GetValue(api);
                    string vstr = v == null ? "" : v.ToString();
                    if (maxSize[property.Name] < vstr.Length)
                    {
                        maxSize[property.Name] = vstr.Length;
                    }
                }
            }

            Console.WriteLine("# API table");

            var types = apis.Where(m => m.Category == Category.Type);
            foreach (var type in types)
            {
                Console.WriteLine();
                Console.WriteLine($"## {type.Name}");
                Console.WriteLine();

                var members = apis.Where(m => m.Parent == type);

                if (members.Count() == 0)
                {
                    Console.WriteLine("There are no newly defined members in this type.");
                    continue;
                }

                Console.Write("|");
                foreach (var property in apiProperty)
                {
                    Console.Write(String.Format($" {{0, -{maxSize[property.Name]}}} |", property.Name));
                }
                Console.WriteLine();
                Console.Write("|");
                foreach (var property in apiProperty)
                {
                    Console.Write(String.Format($" {{0, -{maxSize[property.Name]}}} |", "".PadLeft(maxSize[property.Name], '-')));
                }
                Console.WriteLine();

                foreach (var m in members)
                {
                    Console.Write("|");
                    foreach (var property in apiProperty)
                    {
                        var v = property.GetValue(m);
                        string vstr = v == null ? "" : v.ToString();
                        Console.Write(String.Format($" {{0, -{maxSize[property.Name]}}} |", vstr));
                    }
                    Console.WriteLine();
                }
            }

            PrintHowToMakeIt();
        }

        static void PrintHowToMakeIt()
        {
            Console.WriteLine();
            Console.WriteLine("----");
            Console.WriteLine();
            Console.WriteLine("This document is created with [TCTGen](https://github.com/idkiller/TCTGen.git)");
        }
	}
}
