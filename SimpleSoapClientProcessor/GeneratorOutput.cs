using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XmlSchemaClassGenerator;

namespace SimpleSoapClientProcessor
{
    public class GeneratorOutput : OutputWriter
    {
        private readonly TextWriter tw;
        public GeneratorConfiguration Configuration { get; set; }

        public GeneratorOutput(TextWriter writer)
        {
            tw = writer;
        }

        public override void Write(CodeNamespace cn)
        {
            var cu = new CodeCompileUnit();
            cu.Namespaces.Add(cn);
            base.Write(tw, cu);
        }
    }
}
