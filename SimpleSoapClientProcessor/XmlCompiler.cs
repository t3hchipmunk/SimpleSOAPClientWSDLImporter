using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WsdlNS = System.Web.Services.Description;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Net;
using System.Collections.ObjectModel;
using System.Threading;
using System.Xml.Serialization;
using System.Web.Services.Discovery;
using System.Web.Services.Description;
using System.Runtime.CompilerServices;
using System.Xml.Schema;
using Newtonsoft.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using XmlSchemaClassGenerator;
using System.CodeDom;
using System.Collections;
using System.Xml;

namespace SimpleSoapClientProcessor
{
    public class XmlCompiler
    {
        private DiscoveryClientProtocol DiscoveryClient = new DiscoveryClientProtocol
        {
            AllowAutoRedirect = true
        };

        private DiscoveryDocument DiscoveryClientDocument;
        private ContractReference DiscoveryContractReference;
        private DiscoveryElements DiscoveryClientReferencesElements = new DiscoveryElements();
        private readonly Uri _endpointUri;
        private readonly HttpClient _client;
        private readonly string nameSpace;
        private readonly Uri baseUri;
        private readonly Dictionary<string, XmlSchema> importedSchemas = new Dictionary<string, XmlSchema>();
        private readonly Dictionary<string, XmlSerializerNamespaces> generatedClasses = new Dictionary<string, XmlSerializerNamespaces>();
        private Dictionary<string, string> generatedTypeNamespaces = new Dictionary<string, string>();
        private MessageCollection messages;
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

        NamespaceDeclarationSyntax ns;
        ClassDeclarationSyntax currentClass;

        public XmlCompiler(Uri endpoint, string NameSpace)
        {
            _endpointUri = endpoint;
            baseUri = new Uri(string.Format("{0}://{1}", _endpointUri.Scheme, _endpointUri.Host));

            _client = new HttpClient();

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

            //ns = NamespaceDeclaration(IdentifierName(nameSpace)).WithMembers(new SyntaxList<MemberDeclarationSyntax>());
        }

        public void Start()
        {
            DownloadWSDL();
        }

        private void DownloadWSDL()
        {
            DiscoveryClientDocument = DiscoveryClient.DiscoverAny(_endpointUri.ToString());

            DiscoveryClient.ResolveAll();
            DiscoveryContractReference = DiscoveryClientDocument.References[0] as ContractReference;
            var contract = DiscoveryContractReference.Contract;

            //contract.Types.Schemas.Compile(null, true);

            foreach (DictionaryEntry entry in DiscoveryClient.References)
            {
                if (!(entry.Value is SchemaReference discoveryReference)) { continue; }

                foreach (XmlSchemaObject schemaObject in discoveryReference.Schema.Items)
                {
                    DiscoveryClientReferencesElements.Add(schemaObject);

                }
                //var reference = discoveryReference as SchemaReference;
                //DiscoveryClientReferencesElements.AddRange(reference.Schema.Elements.GetEnumerator().Current);
            }

            messages = contract.Messages as MessageCollection;

            var xmlSchemas = new XmlSchemaSet();
            var ms = new MemoryStream();
            var writer = new StringWriter();
            var generator = new Generator()
            {
                //OutputFolder = "files",
                OutputWriter = new GeneratorOutput(writer),
                CollectionType = typeof(System.Collections.Generic.List<>),
                Log = s => Console.Out.WriteLine(s),
                NamespacePrefix = nameSpace,
                UseXElementForAny = true,
            };

            var locations = new List<string>();
            var services = new List<ServiceDescription>();

            foreach (var document in DiscoveryClient.Documents.Values)
            {
                if (document is XmlSchema schema)
                {
                    if (!string.IsNullOrWhiteSpace(schema.SourceUri))
                    {
                        locations.Add(schema.SourceUri);
                    }
                    xmlSchemas.Add(schema);
                }
                else if (document is ServiceDescription service)
                {
                    services.Add(service);
                }
            }

            generator.Generate(locations.ToArray());

            var tree = CSharpSyntaxTree.ParseText(writer.ToString());
            var root = tree.GetRoot();

            var compilationUnit = root as CompilationUnitSyntax;

            if (compilationUnit is null)
            {
                throw new Exception("XmlSchemaClassGenerator did not produce a valid CSharp code file");
            }

            ns = compilationUnit.Members.First() as NamespaceDeclarationSyntax;

            if (ns == null)
            {
                throw new Exception("XmlSchemaClassGenerator did not produce a valid CSharp namespace as the first node");
            }

            foreach (ServiceDescription serviceDescription in services)
            {
                foreach (Binding binding in serviceDescription.Bindings)
                {
                    //currentMessagesList = serviceDescription.Messages.Cast<Message>().ToList();

                    foreach (OperationBinding operationBinding in binding.Operations)
                    {
                        ParseOperationBinding(operationBinding);
                    }
                }
            }

            var newWriter = new StreamWriter("NewSimpleSoapReference.cs", false, Encoding.UTF8);

            ns.NormalizeWhitespace()
                .WriteTo(newWriter);

            newWriter.Flush();
            newWriter.Close();

            return;

            /*xmlSchemas.Compile(null, true);

            foreach (XmlSchema schema in xmlSchemas)
            {
                foreach (XmlSchemaElement element in schema.Elements.Values)
                {
                    ParseXmlSchemaElement(element);
                }

                foreach (var value in schema.Items)
                {
                    if (value is XmlSchemaComplexType cType)
                    {
                        //ParseXmlSchemaComplexType(cType);
                    }
                    else if (value is XmlSchemaSimpleType sType)
                    {
                        //ParseXmlSchemaSimpleType(sType);
                    }
                }
            }

            var newWriter = new StreamWriter("NewSimpleSoapReference.cs", false, Encoding.UTF8);

            ns.NormalizeWhitespace()
                .WriteTo(newWriter);

            newWriter.Flush();
            newWriter.Close();*/
        }

        private void ParseOperationBinding(OperationBinding operationBinding, [CallerLineNumber] int callerLine = -1)
        {
            currentClass = ClassDeclaration(operationBinding.Name).WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword) }))
                                                                  .WithLeadingTrivia(TriviaList(Comment(string.Format("//{0}:ParseOperationBinding:Sender", callerLine))))
                                                                  .WithBaseList(BaseList(new SeparatedSyntaxList<BaseTypeSyntax>().Add(SimpleBaseType(ParseTypeName("SimpleSoapClient.Models.SimpleSoapClientBase")))));
            ParseOperationBindingExtensions(operationBinding.Extensions);
            ns = ns.AddMembers(currentClass);

            currentClass = ClassDeclaration(operationBinding.Input.Name).WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword) }))
                                                                        .WithLeadingTrivia(TriviaList(Comment(string.Format("//{0}:ParseOperationBinding:Input", callerLine))));
            ParseOperationBindingExtensions(operationBinding.Input.Extensions);
            ns = ns.AddMembers(currentClass);

            currentClass = ClassDeclaration(operationBinding.Output.Name).WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword) }))
                                                                         .WithLeadingTrivia(TriviaList(Comment(string.Format("//{0}:ParseOperationBinding:Output", callerLine))));
            ParseOperationBindingExtensions(operationBinding.Output.Extensions);
            ns = ns.AddMembers(currentClass);
        }

        private void ParseXmlSchemaComplexType(XmlSchemaComplexType type, [CallerLineNumber] int callerLine = -1)
        {
            if (type.ContentModel != null)
            {
                var model = type.ContentModel;
                XmlSchemaObjectCollection attributes = new XmlSchemaObjectCollection();
                if (model.Content is XmlSchemaComplexContentExtension ceContent)
                {
                    attributes = ceContent.Attributes;
                }
                else if (model.Content is XmlSchemaComplexContentRestriction crContent)
                {
                    attributes = crContent.Attributes;
                }
                else if (model.Content is XmlSchemaSimpleContentExtension seContent)
                {
                    attributes = seContent.Attributes;
                }
                else if (model.Content is XmlSchemaSimpleContentRestriction srContent)
                {
                    attributes = srContent.Attributes;
                }

                foreach (XmlSchemaAttribute attr in attributes)
                {
                    ParseXmlSchemaAttribute(attr);
                }

                if (type.ContentType == XmlSchemaContentType.TextOnly)
                {
                    var typeName = GetAliasedType(type.Datatype.ValueType.FullName);

                    var field = FieldDeclaration(VariableDeclaration(ParseTypeName(typeName), new SeparatedSyntaxList<VariableDeclaratorSyntax>()));
                    field = field.AddDeclarationVariables(new[] { VariableDeclarator("valueField") }).WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));

                    currentClass = currentClass.AddMembers(field);

                    var property = PropertyDeclaration(
                        ParseTypeName(typeName), "Value").WithAccessorList(AccessorList(new SyntaxList<AccessorDeclarationSyntax>().AddRange(new AccessorDeclarationSyntax[] { AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ReturnStatement(IdentifierName("valueField"))))), AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName("valueField"), IdentifierName("value")))))) }))).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));

                    currentClass = currentClass.AddMembers(property);
                }

                return;
            }

            var contentParticle = type.ContentTypeParticle;

            if (contentParticle is XmlSchemaSequence)
            {
                foreach (XmlSchemaElement particle in (type.ContentTypeParticle as XmlSchemaSequence).Items)
                {
                    //ParseXmlSchemaElement(particle);
                }
            }
        }

        private void ParseOperationBindingExtensions(ServiceDescriptionFormatExtensionCollection extensions, [CallerLineNumber] int callerLine = -1)
        {
            foreach (ServiceDescriptionFormatExtension extension in extensions)
            {
                if (extension is SoapBodyBinding body)
                {
                    var message = messages[(body.Parent as MessageBinding).Name];

                    //var part = body.Parts[]

                    foreach (MessagePart part in message.Parts)
                    {
                        string typeName = GetAliasedType("object");

                        if (DiscoveryClientReferencesElements.ContainsKey(part.Element.Name))
                        {
                            var elements = DiscoveryClientReferencesElements[part.Element.Name];
                            var xmlObject = elements.Find(e => e is XmlSchemaElement);

                            if ((xmlObject is XmlSchemaElement element))
                            {
                                typeName = GetAliasedType(element.SchemaTypeName.Name);
                            }
                            else if ((xmlObject is XmlSchemaComplexType type))
                            {
                                //typeName = GetAliasedType(type);
                            }
                            else if ((xmlObject is XmlSchemaSimpleType simpleType))
                            {
                                //typeName = GetAliasedType(simpleType.SchemaTypeName.Name);
                            }
                        }

                        var propertyName = part.Element.Name;
                        var escapedPropertyName = FixupIdentifier(propertyName);
                        var fieldName = string.Format("{0}Field", propertyName);

                        var field = FieldDeclaration(VariableDeclaration(ParseTypeName(typeName), new SeparatedSyntaxList<VariableDeclaratorSyntax>())).AddAttributeLists(new AttributeListSyntax[] {
                            AttributeList(
                                SingletonSeparatedList<AttributeSyntax>(
                                    Attribute(IdentifierName("System.Xml.XmlElement")).WithArgumentList(
                                        AttributeArgumentList(
                                            SingletonSeparatedList<AttributeArgumentSyntax>(
                                                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(propertyName)))
                                            )
                                        )
                                    )
                                )
                            )
                        });
                        field = field.AddDeclarationVariables(new[]
                        {
                        VariableDeclarator(fieldName)
                    }).WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));

                        currentClass = currentClass.AddMembers(field);

                        var property = PropertyDeclaration(ParseTypeName(typeName), escapedPropertyName).WithAccessorList(AccessorList(new SyntaxList<AccessorDeclarationSyntax>().AddRange(new AccessorDeclarationSyntax[] { AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ReturnStatement(IdentifierName(fieldName))))), AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName(fieldName), IdentifierName("value")))))) }))).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
                        currentClass = currentClass.AddMembers(property);
                    }
                }
                else if (extension is SoapHeaderBinding header)
                {
                    var message = messages[header.Message.Name];
                    var part = message.FindPartByName(header.Part);

                    string typeName = GetAliasedType("object");

                    if (DiscoveryClientReferencesElements.ContainsKey(part.Name))
                    {
                        var elements = DiscoveryClientReferencesElements[part.Name];
                        var xmlObject = elements.Find(e => e is XmlSchemaElement);

                        if ((xmlObject is XmlSchemaElement element))
                        {
                            typeName = GetAliasedType(element.SchemaTypeName.Name);
                        }
                        else if ((xmlObject is XmlSchemaComplexType type))
                        {
                            //typeName = GetAliasedType(type);
                        }
                        else if ((xmlObject is XmlSchemaSimpleType simpleType))
                        {
                            //typeName = GetAliasedType(simpleType.SchemaTypeName.Name);
                        }
                    }

                    var propertyName = header.Part;
                    var escapedPropertyName = FixupIdentifier(propertyName);
                    var fieldName = string.Format("{0}Field", propertyName);

                    var field = FieldDeclaration(VariableDeclaration(ParseTypeName(typeName), new SeparatedSyntaxList<VariableDeclaratorSyntax>())).AddAttributeLists(new AttributeListSyntax[] {
                            AttributeList(
                                SingletonSeparatedList<AttributeSyntax>(
                                    Attribute(IdentifierName("SimpleSoapClient.Attributes.SoapHeader")).WithArgumentList(
                                        AttributeArgumentList(
                                            SingletonSeparatedList<AttributeArgumentSyntax>(
                                                AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(propertyName)))
                                            )
                                        )
                                    )
                                )
                            )
                        });
                    field = field.AddDeclarationVariables(new[]
                    {
                        VariableDeclarator(fieldName)
                    }).WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));

                    currentClass = currentClass.AddMembers(field);

                    var property = PropertyDeclaration(ParseTypeName(typeName), escapedPropertyName).WithAccessorList(AccessorList(new SyntaxList<AccessorDeclarationSyntax>().AddRange(new AccessorDeclarationSyntax[] { AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ReturnStatement(IdentifierName(fieldName))))), AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName(fieldName), IdentifierName("value")))))) }))).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
                    currentClass = currentClass.AddMembers(property);
                }
                else if (extension is SoapOperationBinding operation)
                {

                    var operationBinding = extension.Parent as OperationBinding;

                    var methodName = QualifiedName(IdentifierName("System.Threading.Tasks"), GenericName(Identifier("Task")).WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(ParseTypeName(GetAliasedType(operationBinding.Output.Name))))));
                    var sendMethod = MethodDeclaration(methodName, Identifier("Send"));
                    sendMethod = sendMethod.WithParameterList(ParameterList(SingletonSeparatedList<ParameterSyntax>(Parameter(Identifier("request")).WithType(ParseTypeName(GetAliasedType(operationBinding.Input.Name))))));

                    var invocationMemberAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("SimpleSoapClient.Models"), IdentifierName("SimpleSoapClientBase"));

                    var memberAccessExpression = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, invocationMemberAccessExpression, GenericName(Identifier("Send")).WithTypeArgumentList(TypeArgumentList(SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[] { IdentifierName(GetAliasedType(operationBinding.Input.Name)), Token(SyntaxKind.CommaToken), IdentifierName(GetAliasedType(operationBinding.Output.Name)) }))));

                    var arguments = ArgumentList(SeparatedList<ArgumentSyntax>(new SyntaxNodeOrToken[] { Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(operationBinding.Name))), Token(SyntaxKind.CommaToken), Argument(IdentifierName("request")) }));

                    var invocationExpression = InvocationExpression(memberAccessExpression).WithArgumentList(arguments);

                    var awaitExpression = AwaitExpression(invocationExpression);

                    sendMethod = sendMethod.WithBody(Block(ReturnStatement(awaitExpression)));
                    sendMethod = sendMethod.WithModifiers(new SyntaxTokenList(new SyntaxToken[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.AsyncKeyword) }));

                    currentClass = currentClass.AddMembers(sendMethod);
                }
            }
        }

        private void ParseXmlSchemaElement(XmlSchemaElement type, [CallerLineNumber] int callerLine = -1)
        {
            var className = FixupClassName(type.Name);

            generatedClasses.Add(className, type.Namespaces);

            currentClass = ClassDeclaration(className).WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword) })).WithLeadingTrivia(TriviaList(Comment(string.Format("//{0}:DownloadWSDL", callerLine))));

            if (type.IsAbstract)
            {
                currentClass = currentClass.AddModifiers(Token(SyntaxKind.AbstractKeyword));
            }

            if (type.ElementSchemaType.BaseXmlSchemaType.Name != null)
            {
                currentClass = currentClass.WithBaseList(BaseList(new SeparatedSyntaxList<BaseTypeSyntax>().Add(SimpleBaseType(ParseTypeName(type.ElementSchemaType.BaseXmlSchemaType.Name)))));
            }

            currentClass = currentClass.AddAttributeLists(new AttributeListSyntax[] { AttributeList(SeparatedList(new AttributeSyntax[] { Attribute(IdentifierName("System.Xml.Serialization.XmlRootAttribute")).WithArgumentList(AttributeArgumentList(SeparatedList(new AttributeArgumentSyntax[] { AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(type.QualifiedName.Name))), AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(type.QualifiedName.Namespace))).WithNameEquals(NameEquals(IdentifierName("Namespace"))) }))) })), AttributeList(SeparatedList(new AttributeSyntax[] { Attribute(IdentifierName("System.Xml.Serialization.XmlTypeAttribute")).WithArgumentList(AttributeArgumentList(SingletonSeparatedList(AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(type.QualifiedName.Namespace))).WithNameEquals(NameEquals(IdentifierName("Namespace")))))) })) });

            var typeName = GetAliasedType(type.ElementSchemaType.Name ?? type.SchemaTypeName.Name);
            var schema = (type.ElementSchemaType ?? type.SchemaType) as XmlSchemaComplexType;


            //schema.ContentModel.Content
            if (schema != null)
            {
                ParseXmlSchemaComplexType(schema);
            }

            /*var propertyName = type.QualifiedName.Name;
            var escapedPropertyName = FixupIdentifier(propertyName);
            var fieldName = string.Format("{0}Field", propertyName);

            var field = FieldDeclaration(VariableDeclaration(ParseTypeName(typeName), new SeparatedSyntaxList<VariableDeclaratorSyntax>()));
            field = field.AddDeclarationVariables(new[] { VariableDeclarator(fieldName) }).WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));

            currentClass = currentClass.AddMembers(field);

            var property = PropertyDeclaration(ParseTypeName(typeName), escapedPropertyName).WithAccessorList(AccessorList(new SyntaxList<AccessorDeclarationSyntax>().AddRange(new AccessorDeclarationSyntax[] { AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ReturnStatement(IdentifierName(fieldName))))), AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName(fieldName), IdentifierName("value")))))) }))).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
            currentClass = currentClass.AddMembers(property);*/

            ns = ns.AddMembers(currentClass);
        }

        private void ParseXmlSchemaAttribute(XmlSchemaAttribute type, [CallerLineNumber] int callerLine = -1)
        {
            var typeName = GetAliasedType(type.AttributeSchemaType.Datatype.ValueType.FullName);
            var propertyName = type.Name;
            var escapedPropertyName = FixupIdentifier(propertyName);
            var fieldName = string.Format("{0}Field", propertyName);

            var field = FieldDeclaration(VariableDeclaration(ParseTypeName(typeName), new SeparatedSyntaxList<VariableDeclaratorSyntax>()));
            field = field.AddDeclarationVariables(new[] { VariableDeclarator(fieldName) })
                         .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));

            currentClass = currentClass.AddMembers(field);

            var property = PropertyDeclaration(
                ParseTypeName(typeName), escapedPropertyName).WithAccessorList(AccessorList(new SyntaxList<AccessorDeclarationSyntax>().AddRange(new AccessorDeclarationSyntax[] { AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ReturnStatement(IdentifierName(fieldName))))), AccessorDeclaration(SyntaxKind.SetAccessorDeclaration, Block(SingletonList<StatementSyntax>(ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName(fieldName), IdentifierName("value")))))) }))).WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
            currentClass = currentClass.AddMembers(property);
        }

        private void ParseXmlSchemaSimpleType(XmlSchemaSimpleType type, [CallerLineNumber] int callerLine = -1)
        {
            if (type.Content is XmlSchemaSimpleTypeRestriction content)
            {
                var enumName = FixupClassName(type.Name);
                generatedClasses.Add(enumName, type.Namespaces);

                var enumSyntax = EnumDeclaration(enumName);
                foreach (XmlSchemaEnumerationFacet facet in content.Facets)
                {
                    enumSyntax = enumSyntax.AddMembers(EnumMemberDeclaration(FixupIdentifier(facet.Value)));
                }

                ns = ns.AddMembers(enumSyntax.AddModifiers(Token(SyntaxKind.PublicKeyword)));
            }
        }

        private async Task<XmlSchemas> ImportXSDs(XmlSchema schemas)
        {
            XmlSchemas imports = new XmlSchemas();

            foreach (XmlSchemaObject externalSchema in schemas.Includes)
            {
                if (externalSchema is XmlSchemaImport)
                {
                    var import = externalSchema as XmlSchemaImport;
                    if (importedSchemas.ContainsKey(import.Namespace))
                    {
                        continue;
                    }

                    Uri schemaUri = new Uri(baseUri, import.SchemaLocation);
                    var response = await _client.GetAsync(schemaUri);

                    var stream = await response.Content.ReadAsStreamAsync();

                    XmlSchema schema = XmlSchema.Read(stream, null);

                    importedSchemas.Add(schema.TargetNamespace, schema);
                    imports.Add(schema);

                    if (schema.Includes.Count > 0)
                    {
                        var addImports = await ImportXSDs(schema);
                        imports.Add(addImports);
                    }
                }
            }

            return imports;
        }

        private string FixupIdentifier(string identifier)
        {
            if ("abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|record|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|virtual|void|volatile|while".Contains(identifier))
            {
                return string.Format("@{0}", identifier);
            }

            return identifier;
        }

        private string GetAliasedType(string type)
        {
            if (type == null)
            {
                return type;
            }

            if (Aliases.ContainsKey(type))
            {
                return Aliases[type];
            }

            return type;
        }

        private string FixupClassName(string name)
        {
            if (!generatedClasses.ContainsKey(name))
            {
                return name;
            }

            var index = 1;
            while (generatedClasses.ContainsKey(name))
            {
                name = string.Format("{0}{1}", name, index);
                index++;
            }

            return name;
        }
    }
}