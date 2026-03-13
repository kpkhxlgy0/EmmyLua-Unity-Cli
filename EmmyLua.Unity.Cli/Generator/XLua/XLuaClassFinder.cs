using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmmyLua.Unity.Generator.XLua;

/// <summary>
/// XLua 类型查找器，支持三种配置方式：
/// 1. 直接在类型上标记 [LuaCallCSharp]
/// 2. 静态字段标记 [LuaCallCSharp]，类型为 List&lt;Type&gt; 或 IEnumerable&lt;Type&gt;
/// 3. 静态属性标记 [LuaCallCSharp]，返回类型为 List&lt;Type&gt; 或 IEnumerable&lt;Type&gt;（动态列表）
/// </summary>
public class XLuaClassFinder
{
    // 静态标志，确保默认类型只添加一次
    private static bool _defaultTypesAdded = false;

    /// <summary>
    /// 获取所有标记为 LuaCallCSharp 的有效类型
    /// </summary>
    public List<INamedTypeSymbol> GetAllValidTypes(Compilation compilation)
    {
        var luaCallCSharpMembers = CollectLuaCallCSharpTypes(compilation, includeDefaultTypes: true);

        return luaCallCSharpMembers
            .Where(type => IsValidType(type))
            .ToList();
    }

    /// <summary>
    /// 获取当前编译单元内所有有效的 public 类型（忽略 LuaCallCSharp 配置）
    /// </summary>
    public List<INamedTypeSymbol> GetAllPublicTypes(Compilation compilation)
    {
        var allPublicTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var luaCallCSharpTypes = CollectLuaCallCSharpTypes(compilation, includeDefaultTypes: false);

        AddDefaultTypesOnce(compilation, allPublicTypes);
        CollectTypesFromNamespace(compilation.Assembly.GlobalNamespace, allPublicTypes);

        return allPublicTypes
            .Where(type => IsValidType(type) &&
                           !IsEditorRelatedType(type) &&
                           IsGenericClassInLuaCallCSharpScope(type, luaCallCSharpTypes))
            .ToList();
    }

    /// <summary>
    /// 收集 LuaCallCSharp 范围内的类型
    /// </summary>
    private HashSet<INamedTypeSymbol> CollectLuaCallCSharpTypes(Compilation compilation, bool includeDefaultTypes)
    {
        var luaCallCSharpMembers = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (includeDefaultTypes) AddDefaultTypesOnce(compilation, luaCallCSharpMembers);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            // 方式1: 查找直接标记在类型上的 [LuaCallCSharp]
            var typeDeclarationSyntaxes = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();
            foreach (var typeDeclaration in typeDeclarationSyntaxes)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
                if (typeSymbol != null && HasLuaCallCSharpAttribute(typeSymbol)) luaCallCSharpMembers.Add(typeSymbol);
            }

            // 方式2 & 3: 查找静态类中标记 [LuaCallCSharp] 的字段和属性
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDeclaration in classDeclarations)
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (classSymbol == null || !classSymbol.IsStatic) continue;

                // 查找标记的字段和属性
                foreach (var member in classSymbol.GetMembers())
                    if (member is IFieldSymbol fieldSymbol &&
                        fieldSymbol.IsStatic &&
                        HasLuaCallCSharpAttribute(fieldSymbol) &&
                        IsEnumerableOfType(fieldSymbol.Type))
                    {
                        var types = AnalyzeMemberForTypes(fieldSymbol, semanticModel);
                        foreach (var type in types) luaCallCSharpMembers.Add(type);
                    }
                    else if (member is IPropertySymbol propertySymbol &&
                             propertySymbol.IsStatic &&
                             HasLuaCallCSharpAttribute(propertySymbol) &&
                             IsEnumerableOfType(propertySymbol.Type))
                    {
                        var types = AnalyzeMemberForTypes(propertySymbol, semanticModel);
                        foreach (var type in types) luaCallCSharpMembers.Add(type);
                    }
            }
        }

        return luaCallCSharpMembers;
    }

    /// <summary>
    /// 添加 XLua 默认导出的类型
    /// </summary>
    private void AddDefaultTypes(Compilation compilation, HashSet<INamedTypeSymbol> types)
    {
        // XLua 默认导出的泛型类型
        var defaultTypeNames = new[]
        {
            "System.Collections.Generic.List`1",
            "System.Collections.Generic.Dictionary`2"
        };

        foreach (var typeName in defaultTypeNames)
        {
            var typeSymbol = compilation.GetTypeByMetadataName(typeName);
            if (typeSymbol != null) types.Add(typeSymbol);
        }
    }

    /// <summary>
    /// 仅首次添加默认导出类型，避免重复
    /// </summary>
    private void AddDefaultTypesOnce(Compilation compilation, HashSet<INamedTypeSymbol> types)
    {
        if (_defaultTypesAdded)
            return;

        AddDefaultTypes(compilation, types);
        _defaultTypesAdded = true;
    }

    /// <summary>
    /// 递归收集命名空间中的类型（包含嵌套类型）
    /// </summary>
    private void CollectTypesFromNamespace(INamespaceSymbol namespaceSymbol, HashSet<INamedTypeSymbol> result)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers()) CollectTypeAndNestedTypes(type, result);
        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            CollectTypesFromNamespace(childNamespace, result);
    }

    /// <summary>
    /// 收集当前类型及其所有嵌套类型
    /// </summary>
    private void CollectTypeAndNestedTypes(INamedTypeSymbol typeSymbol, HashSet<INamedTypeSymbol> result)
    {
        result.Add(typeSymbol);
        foreach (var nestedType in typeSymbol.GetTypeMembers()) CollectTypeAndNestedTypes(nestedType, result);
    }

    /// <summary>
    /// 检查符号是否有 LuaCallCSharp 属性
    /// </summary>
    private bool HasLuaCallCSharpAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "LuaCallCSharpAttribute" ||
            attr.AttributeClass?.Name == "LuaCallCSharp");
    }

    /// <summary>
    /// 检查类型是否为 IEnumerable&lt;Type&gt; 或 List&lt;Type&gt;
    /// </summary>
    private bool IsEnumerableOfType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType) return false;

        // 检查是否是 List<Type> 或 IEnumerable<Type>
        var typeString = namedType.ToString();
        return typeString == "System.Collections.Generic.List<System.Type>" ||
               typeString == "System.Collections.Generic.IEnumerable<System.Type>";
    }

    /// <summary>
    /// 分析字段或属性以提取类型列表
    /// </summary>
    private List<INamedTypeSymbol> AnalyzeMemberForTypes(ISymbol member, SemanticModel semanticModel)
    {
        var result = new List<INamedTypeSymbol>();

        foreach (var syntaxRef in member.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();

            // 分析字段初始化器
            if (syntax is VariableDeclaratorSyntax variableDeclarator)
            {
                if (variableDeclarator.Initializer?.Value is ObjectCreationExpressionSyntax objectCreation)
                    result.AddRange(ExtractTypesFromInitializer(objectCreation, semanticModel));
                else if (variableDeclarator.Initializer?.Value is ImplicitObjectCreationExpressionSyntax
                         implicitCreation)
                    result.AddRange(ExtractTypesFromInitializer(implicitCreation, semanticModel));
            }
            // 分析属性初始化器
            else if (syntax is PropertyDeclarationSyntax propertyDeclaration)
            {
                // 尝试分析 getter 中的返回语句
                var getter = propertyDeclaration.AccessorList?.Accessors
                    .FirstOrDefault(a => a.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration);

                if (getter?.Body != null)
                {
                    var returnStatements = getter.Body.DescendantNodes().OfType<ReturnStatementSyntax>();
                    foreach (var returnStatement in returnStatements)
                        if (returnStatement.Expression is ObjectCreationExpressionSyntax objectCreation)
                            result.AddRange(ExtractTypesFromInitializer(objectCreation, semanticModel));
                }
                // 也支持表达式主体: public static List<Type> Types => new List<Type> { ... };
                else if (propertyDeclaration.ExpressionBody?.Expression is ObjectCreationExpressionSyntax
                         exprBodyCreation)
                {
                    result.AddRange(ExtractTypesFromInitializer(exprBodyCreation, semanticModel));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 从对象创建表达式中提取类型（支持集合初始化器）
    /// </summary>
    private List<INamedTypeSymbol> ExtractTypesFromInitializer(SyntaxNode creationExpression,
        SemanticModel semanticModel)
    {
        var result = new List<INamedTypeSymbol>();

        // 查找所有 typeof(...) 表达式
        var typeofExpressions = creationExpression.DescendantNodes().OfType<TypeOfExpressionSyntax>();
        foreach (var typeofExpr in typeofExpressions)
        {
            var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
            if (typeInfo.Type is INamedTypeSymbol namedType) result.Add(namedType);
        }

        return result;
    }

    /// <summary>
    /// 验证类型是否有效（必须是 public 且非泛型定义）
    /// </summary>
    private bool IsValidType(INamedTypeSymbol type)
    {
        // 必须是 public 类型
        if (type.DeclaredAccessibility != Accessibility.Public)
            return false;

        // 不能是未绑定的泛型类型定义（如 List<>），但可以是构造的泛型类型（如 List<int>）
        if (type.IsUnboundGenericType)
            return false;

        // 不能是编译器生成的类型
        if (type.Name.Contains("<") || type.Name.Contains(">"))
            return false;

        return true;
    }

    /// <summary>
    /// 在 export-all 模式下，仅保留 LuaCallCSharp 范围内的泛型类
    /// </summary>
    private static bool IsGenericClassInLuaCallCSharpScope(INamedTypeSymbol type, HashSet<INamedTypeSymbol> luaCallCSharpTypes)
    {
        if (type.TypeKind != TypeKind.Class || !type.IsGenericType)
            return true;

        if (IsDefaultGenericType(type))
            return true;

        if (luaCallCSharpTypes.Contains(type))
            return true;

        var originalDefinition = type.OriginalDefinition;
        if (luaCallCSharpTypes.Contains(originalDefinition))
            return true;

        return luaCallCSharpTypes.Any(scopeType =>
            SymbolEqualityComparer.Default.Equals(scopeType.OriginalDefinition, originalDefinition));
    }

    private static bool IsDefaultGenericType(INamedTypeSymbol type)
    {
        if (!type.IsGenericType)
            return false;

        var namespaceName = type.ContainingNamespace?.ToDisplayString();
        if (!string.Equals(namespaceName, "System.Collections.Generic", StringComparison.Ordinal))
            return false;

        return type.MetadataName is "List`1" or "Dictionary`2";
    }

    /// <summary>
    /// 是否为 Editor 相关类型（仅用于 --xlua-export-all 路径）
    /// </summary>
    private static bool IsEditorRelatedType(INamedTypeSymbol type)
    {
        if (IsUnityEditorNamespace(type.ContainingNamespace?.ToDisplayString()))
            return true;

        if (IsEditorAssemblyName(type.ContainingAssembly?.Name))
            return true;

        foreach (var location in type.Locations)
            if (location.IsInMetadata && IsEditorAssemblyName(location.MetadataModule?.Name))
                return true;

        return false;
    }

    private static bool IsUnityEditorNamespace(string? namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
            return false;

        return namespaceName.Equals("UnityEditor", StringComparison.OrdinalIgnoreCase) ||
               namespaceName.StartsWith("UnityEditor.", StringComparison.OrdinalIgnoreCase) ||
               namespaceName.EndsWith(".Editor", StringComparison.OrdinalIgnoreCase) ||
               namespaceName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEditorAssemblyName(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return false;

        var normalizedName = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assemblyName[..^4]
            : assemblyName;

        if (normalizedName.Equals("UnityEditor", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.StartsWith("UnityEditor.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedName.EndsWith(".Editor", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalizedName.StartsWith("Assembly-CSharp-Editor", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.StartsWith("Assembly-CSharp-Tests", StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedName.StartsWith("Assembly-", StringComparison.OrdinalIgnoreCase) &&
               (normalizedName.Contains("Editor", StringComparison.OrdinalIgnoreCase) ||
                normalizedName.Contains("Tests", StringComparison.OrdinalIgnoreCase));
    }
}