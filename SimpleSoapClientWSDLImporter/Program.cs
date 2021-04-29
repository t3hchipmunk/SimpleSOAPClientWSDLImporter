using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Description;
using System.Web.Services.Description;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Web.Services.Discovery;
using System.CodeDom;
using System.Web.Services.Protocols;
using System.Web.Compilation;
using System.Net.Http;
using System.Xml.Serialization;
using System.Xml.Schema;
using System.CodeDom.Compiler;
using System.IO;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using SimpleSoapClientProcessor;

namespace SimpleSoapClientWSDLImporter
{
    class Program
    {

        static void Main(string[] args)
        {
            if (args.Length == 0) { throw new Exception("--url needed"); }

            string nameSpace = "";
            string url = "";

            foreach (var arg in args)
            {
                var details = arg.Split('=');

                switch (details[0])
                {
                    case "--namespace":
                        nameSpace = details[1];
                        break;
                    case "--url":
                        url = details[1];
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(url)) { throw new Exception("--url needed"); }

            var compiler = new Compiler(new Uri(url), nameSpace);
            compiler.Start();
        }
    }
}
