﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sharpliner.SourceGenerator;

[AttributeUsage(AttributeTargets.Class)]
public sealed class SharplinerTemplateParametersAttribute : Attribute
{
}

[Generator(LanguageNames.CSharp)]
public class SharplinerTemplateParametersSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var templateParametersDeclarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            typeof(SharplinerTemplateParametersAttribute).FullName!,
            predicate: static (node, ctx) => node is ClassDeclarationSyntax classDeclaration && IsTemplateDefinitionClass(classDeclaration),
            transform: static (syntaxContext, ctx) => GenerateTemplateParameters(syntaxContext, ctx));

        context.RegisterSourceOutput(templateParametersDeclarations, static (ctx, templateDefinitionDetails) =>
        {
            ctx.AddSource($"{templateDefinitionDetails.ClassName}.g.cs", templateDefinitionDetails.Source);
        });
    }

    private static bool IsTemplateDefinitionClass(ClassDeclarationSyntax classDeclaration)
    {
        if (classDeclaration.BaseList?.Types.Count is not 1)
        {
            return false;
        }

        var baseType = classDeclaration.BaseList.Types[0].Type;

        return baseType is GenericNameSyntax genericName && genericName.Identifier.Text is "JobTemplateDefinition" or "StageTemplateDefinition"
            && genericName.TypeArgumentList.Arguments.Count is 1;
    }

    private static TemplateDefinitionDetails GenerateTemplateParameters(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        FileInfo file = new FileInfo(@"C:\github\sharpliner\sharpliner\tests\Sharpliner.Tests\demo.g.cs.txt");
        using var tempWriter = file.CreateText();

        var classDeclaration = (ClassDeclarationSyntax)context.TargetNode;
        var className = classDeclaration.Identifier.Text;

        var builder = new StringBuilder();
        var writer = new IndentedTextWriter(new StringWriter(builder));

        writer.WriteLine("using System;");
        builder.AppendLine();
        var classType = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        writer.WriteLine($"namespace {classType!.ContainingNamespace};");
        WriteTemp(classType.ContainingNamespace);

        WriteTemp(classType);
        WriteTemp(classType.Name);
        writer.WriteLine($"partial class {classType.Name}");
        writer.WriteLine("{");
        writer.Indent++;

        WriteTemp(classDeclaration.BaseList);
        WriteTemp(classDeclaration.BaseList!.Types[0].Type);
        WriteTemp(((GenericNameSyntax)classDeclaration.BaseList!.Types[0].Type).TypeArgumentList);
        WriteTemp(((GenericNameSyntax)classDeclaration.BaseList!.Types[0].Type).TypeArgumentList.Arguments == null);
        WriteTemp(((GenericNameSyntax)classDeclaration.BaseList!.Types[0].Type).TypeArgumentList.Arguments.Count);
        WriteTemp(((GenericNameSyntax)classDeclaration.BaseList!.Types[0].Type).TypeArgumentList.Arguments.First());
        WriteTemp(((GenericNameSyntax)classDeclaration.BaseList!.Types[0].Type).TypeArgumentList.Arguments[0]);

        var parametersClassName = ((GenericNameSyntax)classDeclaration.BaseList!.Types[0].Type).TypeArgumentList.Arguments[0] as SimpleNameSyntax;

        WriteTemp(parametersClassName);

        var parametersClass = context.SemanticModel.GetTypeInfo(parametersClassName).Type;

        WriteTemp(parametersClass);

        var parameters = new List<string>();

        writer.WriteLine($"protected static new readonly {parametersClass.Name}Reference parameters = new();");
        writer.WriteLine($"protected class {parametersClass.Name}Reference : Sharpliner.AzureDevOps.TemplateDefinition.TemplateParameterReference");
        writer.WriteLine("{");
        writer.Indent++;

        WriteTypeProperties(parametersClass, string.Empty);

        // writer.WriteLine($"public override List<Parameter> ToParameters() => [ {string.Join(", ", parameters)} ];");
        writer.Indent--;
        writer.WriteLine("}");
        writer.Indent--;
        writer.WriteLine("}");

        WriteTemp(builder.ToString());

        return new(className, builder.ToString());

        void WriteTemp(object obj, [CallerArgumentExpression(nameof(obj))] string caller = "")
        {
            tempWriter.WriteLine($"{caller} - {obj}");
        }

        void WriteTypeProperties(ITypeSymbol type, string prefix)
        {
            WriteTemp($"{type} - {prefix}");
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>()
                .Where(x => x.Name is not "EqualityContract"))
            {
                var isBuiltInType = IsBuiltInType(property.Type);
                var parameterReferenceType = !isBuiltInType ? property.Type.Name + "ParameterReference" : "Sharpliner.AzureDevOps.ConditionedExpressions.ParameterReference";

                var dataMember = property.GetAttributes().FirstOrDefault(x => x.AttributeClass?.Name is nameof(DataMemberAttribute));
                var parameterName = dataMember?.NamedArguments.FirstOrDefault(x => x.Key is nameof(DataMemberAttribute.Name)).Value.Value?.ToString() ?? property.Name;
                if (prefix.Length > 0)
                {
                    parameterName = $"{prefix}.{parameterName}";
                }

                writer.WriteLine($"public {parameterReferenceType} {property.Name} => new(\"{parameterName}\");");

                if (!isBuiltInType)
                {
                    WriteTemp(property.Type);
                    writer.WriteLine($"public class {parameterReferenceType} : Sharpliner.AzureDevOps.ConditionedExpressions.ParameterReference");
                    writer.WriteLine("{");
                    writer.Indent++;
                    writer.WriteLine($"public {parameterReferenceType}(string parameterName) : base(parameterName) {{ }}");
                    var newPrefix = prefix.Length > 0 ? $"{prefix}.{property.Name}" : property.Name;
                    WriteTypeProperties(property.Type, newPrefix);
                    writer.Indent--;
                    writer.WriteLine("}");
                }
            }

            if (type.BaseType is not null)
            {
                WriteTypeProperties(type.BaseType, prefix);
            }
        }
    }


    private static bool IsBuiltInType(ITypeSymbol type)
    {
        return type.ContainingNamespace.ToString().StartsWith("System");
    }

    private record struct TemplateDefinitionDetails(string ClassName, string Source);
}

