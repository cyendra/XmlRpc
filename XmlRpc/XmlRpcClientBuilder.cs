﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.CodeDom.Compiler;
using System.CodeDom;
using System.Xml.Linq;
using System.Reflection;

namespace XmlRpc
{
    static class XmlRpcClientBuilder
    {
        private static Dictionary<Type, Type> cachedProxyTypes = new Dictionary<Type, Type>();

        private static void CollectType(Type interfaceType, List<Type> types)
        {
            if (!types.Contains(interfaceType))
            {
                types.Add(interfaceType);
                foreach (var type in interfaceType.GetInterfaces())
                {
                    CollectType(type, types);
                }
                if (interfaceType.IsGenericType)
                {
                    foreach (var type in interfaceType.GetGenericArguments())
                    {
                        CollectType(type, types);
                    }
                }
            }
        }

        private static Type CreateProxyType(Type interfaceType)
        {
            CodeDomProvider codedomProvider = CodeDomProvider.CreateProvider("CSharp");
            string namespaceName = "XmlRpc.StringTypedNodeEndpointClientAutoGeneratedClients";
            string typeName = "ImplementationOf" + interfaceType.Name;

            CodeCompileUnit codeCompileUnit = new CodeCompileUnit();
            {
                CodeNamespace codeNamespace = new CodeNamespace(namespaceName);
                codeCompileUnit.Namespaces.Add(codeNamespace);

                CodeTypeDeclaration clientDeclaration = new CodeTypeDeclaration(typeName);
                clientDeclaration.BaseTypes.Add(typeof(XmlRpcClient));
                clientDeclaration.BaseTypes.Add(interfaceType);
                codeNamespace.Types.Add(clientDeclaration);
                {
                    CodeMemberField field = new CodeMemberField(typeof(string), "xmlRpcServiceUrl");
                    clientDeclaration.Members.Add(field);
                    field.Attributes = MemberAttributes.Private;
                }
                {

                    CodeConstructor clientConstructor = new CodeConstructor();
                    clientDeclaration.Members.Add(clientConstructor);
                    clientConstructor.Attributes = MemberAttributes.Public;
                    clientConstructor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), "url"));
                    clientConstructor.Statements.Add(
                        new CodeAssignStatement(
                            new CodeFieldReferenceExpression(
                                new CodeThisReferenceExpression(),
                                "xmlRpcServiceUrl"
                                ),
                            new CodeArgumentReferenceExpression("url")
                            )
                        );
                }
                foreach (var method in interfaceType
                    .GetMethods()
                    .Select(m =>
                        Tuple.Create(
                            m,
                            m.GetCustomAttributes(typeof(XmlRpcMethod), false).Cast<XmlRpcMethod>().FirstOrDefault()
                            )
                        )
                    .Where(m => m.Item2 != null)
                    )
                {
                    var methodInfo = method.Item1;
                    var proxyMethodName = method.Item2.Name;

                    CodeMemberMethod clientMethod = new CodeMemberMethod();
                    clientDeclaration.Members.Add(clientMethod);
                    clientMethod.Attributes = MemberAttributes.Public;
                    clientMethod.Name = methodInfo.Name;
                    clientMethod.ImplementationTypes.Add(methodInfo.DeclaringType);
                    clientMethod.ReturnType = new CodeTypeReference(methodInfo.ReturnType);
                    foreach (var parameterInfo in methodInfo.GetParameters())
                    {
                        clientMethod.Parameters.Add(new CodeParameterDeclarationExpression(parameterInfo.ParameterType, parameterInfo.Name));
                    }

                    clientMethod.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            typeof(XElement),
                            "methodArgument",
                            new CodeMethodInvokeExpression(
                                new CodeMethodReferenceExpression(
                                    new CodeThisReferenceExpression(),
                                    "CreateMethodArguments"
                                    ),
                                new CodePrimitiveExpression(proxyMethodName),
                                new CodeArrayCreateExpression(
                                    typeof(Type),
                                    methodInfo.GetParameters()
                                        .Select(p => new CodeTypeOfExpression(p.ParameterType))
                                        .ToArray()
                                    ),
                                new CodeArrayCreateExpression(
                                    typeof(object),
                                    methodInfo.GetParameters()
                                        .Select(p => new CodeArgumentReferenceExpression(p.Name))
                                        .ToArray()
                                    )
                                )
                            )
                        );

                    clientMethod.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            typeof(XElement),
                            "methodResult",
                            new CodeMethodInvokeExpression(
                                new CodeMethodReferenceExpression(
                                    new CodeThisReferenceExpression(),
                                    "Post"
                                    ),
                                new CodeVariableReferenceExpression("methodArgument"),
                                new CodeFieldReferenceExpression(
                                    new CodeThisReferenceExpression(),
                                    "xmlRpcServiceUrl"
                                    )
                                )
                            )
                        );

                    clientMethod.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            typeof(object),
                            "methodResultObject",
                            new CodeMethodInvokeExpression(
                                new CodeMethodReferenceExpression(
                                    new CodeThisReferenceExpression(),
                                    "CreateMethodResult"
                                    ),
                                new CodeTypeOfExpression(methodInfo.ReturnType),
                                new CodeVariableReferenceExpression("methodResult")
                                )
                            )
                        );

                    clientMethod.Statements.Add(
                        new CodeMethodReturnStatement(
                            new CodeCastExpression(
                                methodInfo.ReturnType,
                                new CodeVariableReferenceExpression("methodResultObject")
                                )
                            )
                        );
                }
            }

            List<Type> interfaceTypes = new List<Type>();
            CollectType(interfaceType, interfaceTypes);

            string[] assemblies = new string[] { typeof(XmlRpcClient).Assembly.Location }
                .Concat(typeof(XmlRpcClient).Assembly.GetReferencedAssemblies().Select(n => Assembly.Load(n).Location))
                .Concat(typeof(XElement).Assembly.GetReferencedAssemblies().Select(n => Assembly.Load(n).Location))
                .Concat(interfaceTypes.Select(t => t.Assembly.Location))
                .Concat(interfaceTypes.SelectMany(t => t.Assembly.GetReferencedAssemblies().Select(n => Assembly.Load(n).Location)))
                .Where(s => s != null)
                .Distinct()
                .ToArray();

            CompilerParameters options = new CompilerParameters();
            options.GenerateExecutable = false;
            options.GenerateInMemory = true;
            options.IncludeDebugInformation = false;
            options.ReferencedAssemblies.AddRange(assemblies);

            CompilerResults result = codedomProvider.CompileAssemblyFromDom(options, codeCompileUnit);
            codedomProvider.Dispose();
            Type clientType = result.CompiledAssembly.GetType(namespaceName + "." + typeName);
            return clientType;
        }

        public static T Create<T>(string url)
        {
            Type serviceType = typeof(T);
            var service = serviceType.GetCustomAttributes(typeof(XmlRpcService), false).Cast<XmlRpcService>().FirstOrDefault();
            if (service == null)
            {
                throw new XmlRpcInvalidContractException("Service interface type should have a XmlRpc.XmlRpcService attribute.");
            }
            if (string.IsNullOrWhiteSpace(url))
            {
                url = service.Url;
            }

            Type proxyType = null;
            if (!cachedProxyTypes.TryGetValue(serviceType, out proxyType))
            {
                proxyType = CreateProxyType(serviceType);
                cachedProxyTypes.Add(serviceType, proxyType);
            }
            object proxy = proxyType.GetConstructor(new Type[] { typeof(string) }).Invoke(new object[] { url });
            return (T)proxy;
        }
    }
}
