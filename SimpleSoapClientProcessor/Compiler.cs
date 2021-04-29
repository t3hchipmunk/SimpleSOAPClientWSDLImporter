using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Web.Services.Discovery;
using System.Xml.Schema;
using System.Web.Services.Description;
using System.Linq;
using System.IO;
using XmlSchemaClassGenerator;
using System.CodeDom;

namespace SimpleSoapClientProcessor
{
    public class Compiler
    {
        private readonly DiscoveryClientProtocol DiscoveryClient = new DiscoveryClientProtocol
        {
            AllowAutoRedirect = true
        };

        private DiscoveryDocument DiscoveryClientDocument;
        private ContractReference DiscoveryContractReference;
        private readonly DiscoveryElements DiscoveryClientReferencesElements = new DiscoveryElements();

        private readonly Uri _endpointUri;

        private readonly string nameSpace;

        private readonly Dictionary<string, string> Aliases = new Dictionary<string, string>()
        {
            { typeof(byte).FullName, "byte" },
            { typeof(sbyte).FullName, "sbyte" },
            { typeof(short).FullName, "short" },
            { typeof(ushort).FullName, "ushort" },
            { typeof(int).FullName, "int" },
            { typeof(uint).FullName, "uint" },
            { typeof(long).FullName, "long" },
            { typeof(ulong).FullName, "ulong" },
            { typeof(float).FullName, "float" },
            { typeof(double).FullName, "double" },
            { typeof(decimal).FullName, "decimal" },
            { typeof(object).FullName, "object" },
            { typeof(bool).FullName, "bool" },
            { "boolean", "bool" },
            { typeof(char).FullName, "char" },
            { typeof(string).FullName, "string" },
            { typeof(void).FullName, "void" },
            { "dateTime", "System.DateTime" },
            { "base64Binary", "byte[]" }
        };

        public Compiler(Uri endpoint, string NameSpace)
        {
            _endpointUri = endpoint;

            nameSpace = NameSpace;

            if (string.IsNullOrEmpty(nameSpace))
            {
                var hostParts = _endpointUri.Host.Split('.');

                for (int index = hostParts.Length - 1; index > 0; index--)
                {
                    nameSpace += string.Format("{0}.", hostParts[index]);
                }

                nameSpace = nameSpace.Substring(0, nameSpace.Length - 1);
            }
        }

        public void Start()
        {
            DownloadWSDL();
        }

        private void DownloadWSDL()
        {
            NamespaceDeclarationSyntax ns = NamespaceDeclaration(IdentifierName(nameSpace));

            DiscoveryClientDocument = DiscoveryClient.DiscoverAny(_endpointUri.ToString());

            DiscoveryClient.ResolveAll();
            DiscoveryContractReference = DiscoveryClientDocument.References[0] as ContractReference;

            var schemas = new XmlSchemaSet();

            foreach (DictionaryEntry entry in DiscoveryClient.References)
            {
                if (!(entry.Value is SchemaReference discoveryReference)) { continue; }

                foreach (XmlSchemaObject schemaObject in discoveryReference.Schema.Items)
                {
                    DiscoveryClientReferencesElements.Add(schemaObject);
                }

                schemas.Add(discoveryReference.Schema.TargetNamespace, discoveryReference.Ref);
            }

            var headers = DiscoveryContractReference.Contract.Messages["headers"];

            var ms = new MemoryStream();
            var writer = new StringWriter();
            
            var generator = new Generator()
            {
                OutputWriter = new GeneratorOutput(writer),
                CollectionType = typeof(System.Collections.Generic.List<>),
                Log = s => Console.Out.WriteLine(s),
                NamespacePrefix = nameSpace,
                NamingScheme = NamingScheme.Direct,
                UniqueTypeNamesAcrossNamespaces = true,
                GenerateNullables = true,
                CollectionSettersMode = CollectionSettersMode.PublicWithoutConstructorInitialization,
                EntityFramework = true,
                TypeVisitor = (type, model) =>
                {
                    if ((!type.IsClass) && !(type.IsInterface)) { return; }

                    foreach (CodeAttributeDeclaration attribute in type.CustomAttributes)
                    {
                        if (attribute.Name != "System.Xml.Serialization.XmlRootAttribute") { continue; }

                        foreach (CodeAttributeArgument argument in attribute.Arguments)
                        {
                            if (argument.Name != "") { continue; }

                            var partname = (argument.Value as CodePrimitiveExpression).Value.ToString();
                            try
                            {
                                headers.FindPartByName(partname);
                                type.BaseTypes.Add(new CodeTypeReference("SimpleSOAPClient.Models.SoapHeader"));

                                return;
                            }
                            catch { }
                        }
                    }
                }
            };

            generator.Generate(schemas);

            var tree = CSharpSyntaxTree.ParseText(writer.ToString());
            var root = tree.GetRoot();

            if (!(root is CompilationUnitSyntax compilationUnit))
            {
                throw new Exception("XmlSchemaClassGenerator did not produce a valid CSharp code file");
            }

            ns = compilationUnit.Members.First() as NamespaceDeclarationSyntax;

            if (ns == null)
            {
                throw new Exception("XmlSchemaClassGenerator did not produce a valid CSharp namespace as the first node");
            }

            DiscoveryContractReference.Contract.Types.Schemas.Compile(null, true);
            foreach (Binding binding in DiscoveryContractReference.Contract.Bindings)
            {

                var portClass = ClassDeclaration(string.Format("{0}Client", binding.Type.Name)).WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword) }));

                foreach (OperationBinding operation in binding.Operations)
                {

                    var inputMessage = DiscoveryContractReference.Contract.Messages[operation.Input.Name];
                    var outputMessage = DiscoveryContractReference.Contract.Messages[operation.Output.Name];

                    var inputPart = inputMessage.FindPartByName("parameters");
                    var outputPart = outputMessage.FindPartByName("parameters");

                    var inputElement = DiscoveryClientReferencesElements.Elements[inputPart.Element.Name];
                    var outputElement = DiscoveryClientReferencesElements.Elements[outputPart.Element.Name];

                    var inputType = string.Format("{0}.{1}", nameSpace, inputElement.SchemaTypeName.Name);
                    var outputType = string.Format("{0}.{1}", nameSpace, outputElement.SchemaTypeName.Name);

                    var methodName = QualifiedName(IdentifierName("System.Threading.Tasks"), GenericName(Identifier("Task")).WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(ParseTypeName(GetAliasedType(outputType))))));
                    var operationMethod = MethodDeclaration(methodName, Identifier(operation.Name));

                    var headerCollectionDeclaration = VariableDeclaration(
                            ArrayType(IdentifierName("SimpleSOAPClient.Models.SoapHeader")).WithRankSpecifiers(
                                SingletonList<ArrayRankSpecifierSyntax>(
                                    ArrayRankSpecifier(
                                        SingletonSeparatedList<ExpressionSyntax>(
                                            OmittedArraySizeExpression())))));

                    var headerVariable = VariableDeclarator(Identifier("headers"));
                    
                    var headerArrayInitializer = InitializerExpression(SyntaxKind.ArrayInitializerExpression);
                    
                    SyntaxList<StatementSyntax> bodyStatements = new SyntaxList<StatementSyntax>();
                    
                    foreach (ServiceDescriptionFormatExtension extension in operation.Input.Extensions)
                    {
                        ParameterSyntax parameter;
                        if (extension is SoapHeaderBinding header)
                        {
                            var headerMessage = DiscoveryContractReference.Contract.Messages[header.Message.Name];
                            var headerPart = headerMessage.FindPartByName(header.Part);
                            var headerElement = DiscoveryClientReferencesElements.Elements[headerPart.Element.Name];
                            var headerType = string.Format("{0}.{1}", nameSpace, headerElement.SchemaTypeName.Name);
                            parameter = Parameter(Identifier(header.Part)).WithType(ParseTypeName(GetAliasedType(headerType)));

                            operationMethod = operationMethod.AddParameterListParameters(parameter);
                            headerArrayInitializer = headerArrayInitializer.AddExpressions(IdentifierName(header.Part));

                        }
                        else if (extension is SoapBodyBinding body)
                        {
                            parameter = Parameter(Identifier("request")).WithType(ParseTypeName(GetAliasedType(inputType)));

                            operationMethod = operationMethod.AddParameterListParameters(parameter);
                        }
                    }

                    headerVariable = headerVariable.WithInitializer(EqualsValueClause(headerArrayInitializer));

                    headerCollectionDeclaration = headerCollectionDeclaration.AddVariables(headerVariable);
                    
                    var headerCollectionBlock = LocalDeclarationStatement(headerCollectionDeclaration);

                    bodyStatements = bodyStatements.Add(headerCollectionBlock);
                    bodyStatements = bodyStatements.Add(EmptyStatement().WithSemicolonToken(MissingToken(SyntaxKind.SemicolonToken)));

                    var invocationMemberAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("SimpleSOAPClient.Models"), IdentifierName("SimpleSOAPClientBase"));

                    var memberAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, invocationMemberAccessExpression, GenericName(Identifier("Send")).WithTypeArgumentList(TypeArgumentList(SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[] { IdentifierName(GetAliasedType(inputType)), Token(SyntaxKind.CommaToken), IdentifierName(GetAliasedType(outputType)) }))));

                    var arguments = ArgumentList(SeparatedList<ArgumentSyntax>(new SyntaxNodeOrToken[] {
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(operation.Name))), Token(SyntaxKind.CommaToken), Argument(IdentifierName("request")), Token(SyntaxKind.CommaToken), Argument(IdentifierName("headers"))
                    }));

                    var invocationExpression = InvocationExpression(memberAccessExpression).WithArgumentList(arguments);

                    var awaitExpression = AwaitExpression(invocationExpression);

                    bodyStatements = bodyStatements.Add(ReturnStatement(awaitExpression));

                    operationMethod = operationMethod.WithBody(Block(bodyStatements));
                    
                    operationMethod = operationMethod.WithModifiers(new SyntaxTokenList(new SyntaxToken[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.AsyncKeyword) }));

                    portClass = portClass.AddMembers(operationMethod);
                }

                ns = ns.AddMembers(portClass);
            }

            var newWriter = new StreamWriter("NewSimpleSoapReference.cs", false, Encoding.UTF8);

            ns.NormalizeWhitespace()
                .WriteTo(newWriter);

            newWriter.Flush();
            newWriter.Close();
        }

        private string GetAliasedType(string type)
        {
            if (Aliases.ContainsKey(type))
            {
                return Aliases[type];
            }

            return type;
        }
    }
}
