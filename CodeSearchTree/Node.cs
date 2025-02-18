﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System.ComponentModel;

namespace CodeSearchTree
{
    public class Node
    {
        [Category("Meta"), Description("Enumeration of Roslyn types.")]
        public NodeType NodeType { get; internal set; }

        [Category("Relatives"), Description("List of child nodes.")]
        public NodeList Children { get; }

        [Category("Main"), Description("Original source code.")]
        public string Source { get; internal set; }

        [Category("Main"), Description("Start character position.")]
        public int StartPosition { get; internal set; }

        [Category("Main"), Description("End character position.")]
        public int EndPosition { get; internal set; }

        [Category("Meta"), Description("List of leading trivia.")]
        public TriviaList LeadingTrivia { get; }

        [Category("Meta"), Description("List of trailing trivia.")]
        public TriviaList TrailingTrivia { get; }

        [Category("Meta"), Description("Name or identifier of node.")]
        public string Name { get; private set; }

        [Category("Relatives"), Description("Reference to parent node, if available.")]
        public Node Parent { get; }

        internal NodeList ParentListIfNoParent { get; set; }
        public object RoslynNode { get; }

        protected internal Node(object roslynNode, string source)
            : this(roslynNode, source, null, NodeType.NamespaceDeclarationSyntaxNode)
        {
        }

        protected internal Node(object roslynNode, string source, Node parent, NodeType node_type)
        {
            RoslynNode = roslynNode;
            StartPosition = (roslynNode as SyntaxNode).FullSpan.Start;
            EndPosition = (roslynNode as SyntaxNode).FullSpan.End;
            NodeType = node_type;
            Children = new NodeList();
            LeadingTrivia = new TriviaList();
            TrailingTrivia = new TriviaList();
            Parent = parent;
            Source = source;
        }

        [Category("Main"), Description("Original source length in characters.")]
        public int Length => this.Source.Length;

        [Category("Relatives"), Description("Number of child nodes.")]
        public int ChildCount => Children.Count;

        [Category("Relatives"), Description("Type of parant node, if available.")]
        public NodeType ParentType => Parent?.NodeType ?? NodeType.UnknownNode;

        [Category("Meta"), Description("String representation of leading trivia.")]
        public string LeadingTriviaString
        {
            get
            {
                var s = new StringBuilder();
                LeadingTrivia.ForEach(x => s.Append(x.Source));
                return s.ToString();
            }
        }

        [Category("Meta"), Description("String representation of trailing trivia.")]
        public string TrailingTriviaString
        {
            get
            {
                var s = new StringBuilder();
                this.TrailingTrivia.ForEach(x => s.Append(x.Source));
                return s.ToString();
            }
        }

        [Category("Roslyn"), Description("Properties of the underlying Roslyn SyntaxNode.")]
        public List<Property> RoslynNodeProperties
        {
            get
            {
                var ret = new List<Property>();
                var n = this.RoslynNode as SyntaxNode;
                if (n != null)
                {
                    var properties = n.GetType().GetProperties();
                    properties.ToList().ForEach(x => ret.Add(new Property(x.Name, x.GetValue(n))));
                }
                return ret;
            }
        }

        [Category("Roslyn"), Description("String representation of the properties of the underlying Roslyn SyntaxNode.")
        ]
        public string RoslynNodePropertiesString
        {
            get
            {
                var s = new StringBuilder();
                RoslynNodeProperties.ForEach(x => s.AppendLine(x.ToString()));
                return s.ToString();
            }
        }

        public static NodeList CreateTreeFromFile(string filename)
        {
            var code = "";
            using (var sr = new System.IO.StreamReader(filename, Encoding.UTF8))
                code = sr.ReadToEnd();
            return CreateTreeFromCode(code);
        }

        public static NodeList CreateTreeFromCode(string code)
        {
            var ret = new NodeList();
            var tree = CSharpSyntaxTree.ParseText(code).GetRoot();
            foreach (var n in tree.ChildNodes())
            {
                var codeNode = new Node(n, n.ToString(), null, GetNodeType(n)) {ParentListIfNoParent = ret};
                StoreTrivia(codeNode, n);
                ret.Add(codeNode);
                CreateChildren(codeNode, n);
            }
            foreach (var n in ret)
                n.CheckName();
            return ret;
        }

        private void CheckName()
        {
            var n = RoslynNode as SyntaxNode;
            if (n != null)
            {
                if (n is ClassDeclarationSyntax)
                    Name = (n as ClassDeclarationSyntax).Identifier.ToString();
                else if (n is NamespaceDeclarationSyntax)
                    Name = (n as NamespaceDeclarationSyntax).Name.ToString();
                else if (n is FieldDeclarationSyntax)
                {
                    var v = GetChild("vardeclaration/id");
                    Name = (v?.RoslynNode as IdentifierNameSyntax)?.ToString() ?? "";
                }
                else if (n is VariableDeclarationSyntax || n is PropertyDeclarationSyntax)
                {
                    var v = GetChild("id");
                    Name = (v?.RoslynNode as IdentifierNameSyntax)?.ToString() ?? "";
                }
                else if (n is IdentifierNameSyntax)
                    Name = n.ToString();
                else if (n is MethodDeclarationSyntax)
                    Name = (n as MethodDeclarationSyntax).Identifier.ToString();
                else if (n is UsingDirectiveSyntax)
                {
                    var id = GetChild("name");
                    if (id == null)
                    {
                        id = GetChild("id");
                        Name = (id?.RoslynNode as IdentifierNameSyntax)?.ToString() ?? "";
                    }
                    else
                        Name = (id.RoslynNode as QualifiedNameSyntax).ToString();
                }
            }
            if (string.IsNullOrWhiteSpace(Name))
                Name = "";
            foreach (var child in Children)
                child.CheckName();
        }

        private static void CreateChildren(Node node, SyntaxNode roslynNode)
        {
            foreach (var n in roslynNode.ChildNodes())
            {
                var codeNode = new Node(n, n.ToString(), node, GetNodeType(n));
                StoreTrivia(codeNode, n);
                node.Children.Add(codeNode);
                CreateChildren(codeNode, n);
            }
        }

        /// <summary>
        /// Search expression that allocates this node from root.
        /// </summary>
        [Category("Allocation")]
        public string FullPath
        {
            get
            {
                var ret = "";
                //Hämta sökvägen från parent tills dess att parent är null.
                Node currentParent;
                var currentThis = this;
                do
                {
                    //Växla till nästa förälder.
                    currentParent = currentThis.Parent;
                    if (currentParent == null)
                    {
                        //Ingen mer parent. Kolla var vi ligger i root-listan.
                        if (currentThis.ParentListIfNoParent == null)
                            ret =
                                $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}{(ret == "" ? "" : "/")}{ret}";
                        else
                        {
                            //Filtrera ut så att vi har rätt typ, därefter ta reda på index.
                            var index =
                                currentThis.ParentListIfNoParent.Where(x => x.NodeType == currentThis.NodeType)
                                    .ToList()
                                    .IndexOf(currentThis);
                            //Om index är > 0, presentera det som en [vakt].
                            ret = index <= 0
                                ? $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}{(ret == "" ? "" : "/")}{ret}"
                                : $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}[{index}]{(ret == "" ? "" : "/")}{ret}";
                        }
                    }
                    else
                    {
                        var index =
                            currentParent.Children.Where(x => x.NodeType == currentThis.NodeType)
                                .ToList()
                                .IndexOf(currentThis);
                        //Om index är > 0, presentera det som en [vakt].
                        ret = index <= 0
                            ? $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}{(ret == "" ? "" : "/")}{ret}"
                            : $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}[{index}]{(ret == "" ? "" : "/")}{ret}";
                    }
                    //Växla till nästa child.
                    currentThis = currentParent;
                } while (currentParent != null);
                return ret;
            }
        }

        /// <summary>
        /// Same as FullPath but with name guards where possible.
        /// </summary>
        [Category("Allocation")]
        public string PossibleAlternativePath
        {
            get
            {
                var ret = "";
                //Hämta sökvägen från parent tills dess att parent är null.
                Node currentParent;
                var currentThis = this;
                do
                {
                    //Växla till nästa förälder.
                    currentParent = currentThis.Parent;
                    if (currentParent == null)
                    {
                        //Ingen mer parent. Kolla var vi ligger i root-listan.
                        if (currentThis.Name == "")
                        {
                            if (currentThis.ParentListIfNoParent == null)
                                ret =
                                    $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}{(ret == "" ? "" : "/")}{ret}";
                            else
                            {
                                //Filtrera ut så att vi har rätt typ, därefter ta reda på index.
                                var index =
                                    currentThis.ParentListIfNoParent.Where(x => x.NodeType == currentThis.NodeType)
                                        .ToList()
                                        .IndexOf(currentThis);
                                //Om index är > 0, presentera det som en [vakt].
                                ret = index <= 0
                                    ? $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}{(ret == "" ? "" : "/")}{ret}"
                                    : $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}[{index}]{(ret == "" ? "" : "/")}{ret}";
                            }
                        }
                        else
                        {
                            ret =
                                $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}[{currentThis.Name}]{(ret == "" ? "" : "/")}{ret}";
                        }
                    }
                    else
                    {
                        if (currentThis.Name == "")
                        {
                            var index =
                                currentParent.Children.Where(x => x.NodeType == currentThis.NodeType)
                                    .ToList()
                                    .IndexOf(currentThis);
                            //Om index är > 0, presentera det som en [vakt].
                            ret = index <= 0
                                ? $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}{(ret == "" ? "" : "/")}{ret}"
                                : $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}[{index}]{(ret == "" ? "" : "/")}{ret}";
                        }
                        else
                        {
                            ret =
                                $"{SearchExpressionParser.NodeTypeToKeyword(currentThis.NodeType)}[{currentThis.Name}]{(ret == "" ? "" : "/")}{ret}";
                        }
                    }
                    //Växla till nästa child.
                    currentThis = currentParent;
                } while (currentParent != null);
                return ret;
            }
        }

        private static NodeType GetNodeType(SyntaxNode n)
        {
            if (n is UsingDirectiveSyntax)
                return NodeType.UsingDirectiveSyntaxNode;
            if (n is NamespaceDeclarationSyntax)
                return NodeType.NamespaceDeclarationSyntaxNode;
            if (n is ClassDeclarationSyntax)
                return NodeType.ClassDeclarationSyntaxNode;
            if (n is IdentifierNameSyntax)
                return NodeType.IdentifierNameSyntaxNode;
            if (n is QualifiedNameSyntax)
                return NodeType.QualifiedNameSyntaxNode;
            if (n is FieldDeclarationSyntax)
                return NodeType.FieldDeclarationSyntaxNode;
            if (n is VariableDeclarationSyntax)
                return NodeType.VariableDeclarationSyntaxNode;
            if (n is VariableDeclaratorSyntax)
                return NodeType.VariableDeclaratorSyntaxNode;
            if (n is PropertyDeclarationSyntax)
                return NodeType.PropertyDeclarationSyntaxNode;
            if (n is AccessorListSyntax)
                return NodeType.AccessorListSyntaxNode;
            if (n is AccessorDeclarationSyntax)
                return NodeType.AccessorDeclarationSyntaxNode;
            if (n is AttributeListSyntax)
                return NodeType.AttributeListSyntaxNode;
            if (n is AttributeSyntax)
                return NodeType.AttributeSyntaxNode;
            if (n is AttributeArgumentListSyntax)
                return NodeType.AttributeArgumentListSyntaxNode;
            if (n is BlockSyntax)
                return NodeType.BlockSyntaxNode;
            if (n is ReturnStatementSyntax)
                return NodeType.ReturnStatementSyntaxNode;
            if (n is MethodDeclarationSyntax)
                return NodeType.MethodDeclarationSyntaxNode;
            if (n is PredefinedTypeSyntax)
                return NodeType.PredefinedTypeSyntaxNode;
            if (n is ParameterListSyntax)
                return NodeType.ParameterListSyntaxNode;
            if (n is ExpressionStatementSyntax)
                return NodeType.ExpressionStatementSyntaxNode;
            if (n is InvocationExpressionSyntax)
                return NodeType.InvocationExpressionSyntaxNode;
            if (n is ArgumentListSyntax)
                return NodeType.ArgumentListSyntaxNode;
            if (n is AssignmentExpressionSyntax)
                return NodeType.AssignmentExpressionSyntaxNode;
            if (n is MemberAccessExpressionSyntax)
                return NodeType.MemberAccessExpressionSyntaxNode;
            if (n is SwitchStatementSyntax)
                return NodeType.SwitchStatementSyntaxNode;
            if (n is ArgumentSyntax)
                return NodeType.ArgumentSyntaxNode;
            if (n is LiteralExpressionSyntax)
                return NodeType.LiteralExpressionSyntaxNode;
            if (n is IfStatementSyntax)
                return NodeType.IfStatementSyntaxNode;
            if (n is PrefixUnaryExpressionSyntax)
                return NodeType.PrefixUnaryExpressionSyntaxNode;
            if (n is ParenthesizedExpressionSyntax)
                return NodeType.ParenthesizedExpressionSyntaxNode;
            if (n is BinaryExpressionSyntax)
                return NodeType.BinaryExpressionSyntaxNode;
            if (n is ElseClauseSyntax)
                return NodeType.ElseClauseSyntaxNode;
            if (n is WhileStatementSyntax)
                return NodeType.WhileStatementSyntaxNode;
            if (n is BreakStatementSyntax)
                return NodeType.BreakStatementSyntaxNode;
            if (n is UsingStatementSyntax)
                return NodeType.UsingStatementSyntaxNode;
            if (n is ForStatementSyntax)
                return NodeType.ForStatementSyntaxNode;
            if (n is LabeledStatementSyntax)
                return NodeType.LabeledStatementSyntaxNode;
            if (n is BaseListSyntax)
                return NodeType.BaseListSyntaxNode;
            if (n is SimpleBaseTypeSyntax)
                return NodeType.SimpleBaseTypeSyntaxNode;
            if (n is GenericNameSyntax)
                return NodeType.GenericNameSyntaxNode;
            if (n is TypeArgumentListSyntax)
                return NodeType.TypeArgumentListSyntaxNode;
            if (n is ParameterSyntax)
                return NodeType.ParameterSyntaxNode;
            if (n is LocalDeclarationStatementSyntax)
                return NodeType.LocalDeclarationStatementSyntaxNode;
            if (n is EqualsValueClauseSyntax)
                return NodeType.EqualsValueClauseSyntaxNode;
            if (n is ObjectCreationExpressionSyntax)
                return NodeType.ObjectCreationExpressionSyntaxNode;
            if (n is TypeOfExpressionSyntax)
                return NodeType.TypeOfExpressionSyntaxNode;
            if (n is ThrowStatementSyntax)
                return NodeType.ThrowStatementSyntaxNode;
            if (n is ThisExpressionSyntax)
                return NodeType.ThisExpressionSyntaxNode;
            if (n is SimpleLambdaExpressionSyntax)
                return NodeType.SimpleLambdaExpressionSyntaxNode;
            if (n is ForEachStatementSyntax)
                return NodeType.ForEachStatementSyntaxNode;
            if (n is TryStatementSyntax)
                return NodeType.TryStatementSyntaxNode;
            if (n is CatchClauseSyntax)
                return NodeType.CatchClauseSyntaxNode;
            if (n is SwitchSectionSyntax)
                return NodeType.SwitchSectionSyntaxNode;
            if (n is CaseSwitchLabelSyntax)
                return NodeType.CaseSwitchLabelSyntaxNode;
            if (n is DefaultSwitchLabelSyntax)
                return NodeType.DefaultSwitchLabelSyntaxNode;
            if (n is ArrayTypeSyntax)
                return NodeType.ArrayTypeSyntaxNode;
            if (n is ArrayRankSpecifierSyntax)
                return NodeType.ArrayRankSpecifierSyntaxNode;
            if (n is OmittedArraySizeExpressionSyntax)
                return NodeType.OmittedArraySizeExpressionSyntaxNode;
            if (n is ElementAccessExpressionSyntax)
                return NodeType.ElementAccessExpressionSyntaxNode;
            if (n is BracketedArgumentListSyntax)
                return NodeType.BracketedArgumentListSyntaxNode;
            if (n is ConditionalExpressionSyntax)
                return NodeType.ConditionalExpressionSyntaxNode;
            if (n is PostfixUnaryExpressionSyntax)
                return NodeType.PostfixUnaryExpressionSyntaxNode;
            if (n is ContinueStatementSyntax)
                return NodeType.ContinueStatementSyntaxNode;
            if (n is ConstructorDeclarationSyntax)
                return NodeType.ConstructorDeclarationSyntaxNode;
            if (n is QueryExpressionSyntax)
                return NodeType.QueryExpressionSyntaxNode;
            if (n is FromClauseSyntax)
                return NodeType.FromClauseSyntaxNode;
            if (n is QueryBodySyntax)
                return NodeType.QueryBodySyntaxNode;
            if (n is WhereClauseSyntax)
                return NodeType.WhereClauseSyntaxNode;
            if (n is SelectClauseSyntax)
                return NodeType.SelectClauseSyntaxNode;
            if (n is DoStatementSyntax)
                return NodeType.DoStatementSyntaxNode;
            if (n is NameEqualsSyntax)
                return NodeType.NameEqualsSyntaxNode;
            if (n is EnumDeclarationSyntax)
                return NodeType.EnumDeclarationSyntaxNode;
            if (n is EnumMemberDeclarationSyntax)
                return NodeType.EnumMemberDeclarationSyntaxNode;
            if (n is AttributeArgumentSyntax)
                return NodeType.AttributeArgumentSyntaxNode;
            if (n is ConstructorInitializerSyntax)
                return NodeType.ConstructorInitializerSyntaxNode;
            if (n is EmptyStatementSyntax)
                return NodeType.EmptyStatementSyntaxNode;
            if (n is InitializerExpressionSyntax)
                return NodeType.InitializerExpressionSyntaxNode;
            if (n is AwaitExpressionSyntax)
                return NodeType.AwaitExpressionSyntaxNode;
            if (n is AnonymousObjectCreationExpressionSyntax)
                return NodeType.AnonymousObjectCreationExpressionSyntaxNode;
            if (n is AnonymousObjectMemberDeclaratorSyntax)
                return NodeType.AnonymousObjectMemberDeclaratorSyntaxNode;
            if (n is TypeParameterListSyntax)
                return NodeType.TypeParameterListSyntaxNode;
            if (n is TypeParameterSyntax)
                return NodeType.TypeParameterSyntaxNode;
            if (n is DefaultExpressionSyntax)
                return NodeType.DefaultExpressionSyntaxNode;
            if (n is InterfaceDeclarationSyntax)
                return NodeType.InterfaceDeclarationSyntaxNode;
            if (n is CastExpressionSyntax)
                return NodeType.CastExpressionSyntaxNode;
            if (n is BaseExpressionSyntax)
                return NodeType.BaseExpressionSyntaxNode;
            if (n is AttributeTargetSpecifierSyntax)
                return NodeType.AttributeTargetSpecifierSyntaxNode;
            if (n is AliasQualifiedNameSyntax)
                return NodeType.AliasQualifiedNameSyntaxNode;
            if (n is ExplicitInterfaceSpecifierSyntax)
                return NodeType.ExplicitInterfaceSpecifierSyntaxNode;
#if DEBUG
            Console.WriteLine(n.GetType().Name);
            var code = n.ToString().Length > 40 ? n.ToString().Substring(0, 40) : n.ToString();
            Console.WriteLine(code);
            throw new Exception(n.GetType().Name);
#else
            return NodeType.UnknownNode;
#endif
        }

        private static void StoreTrivia(Node node, SyntaxNode roslynNode)
        {
            foreach (var t in roslynNode.GetLeadingTrivia())
            {
                var s = t.ToString().Trim();
                if (s != "")
                    node.LeadingTrivia.Add(new Trivia(GetTriviaType(t), s));
            }
            foreach (var t in roslynNode.GetTrailingTrivia())
            {
                var s = t.ToString().Trim();
                if (s != "")
                    node.TrailingTrivia.Add(new Trivia(GetTriviaType(t), s));
            }
        }

        public NodeList GetChildren(params NodeType[] type)
        {
            if (type.Length <= 0)
                return new NodeList();
            if (type.Length == 1)
                return Children.FilterByNameOrIndexOrType(type[0]);
            if (type.Length == 2)
            {
                var ret = new NodeList();
                var item = Children.FilterByNameOrIndexOrType(type[0]).FirstOrDefault();
                if (item == null)
                    return ret;
                ret.AddRange(item.Children.FilterByNameOrIndexOrType(type[1]));
                return ret;
            }
            else
            {
                var item = Children.FilterByNameOrIndexOrType(type[0]).FirstOrDefault();
                for (int i = 1; i < type.Length - 1; i++)
                {
                    if (item == null)
                        return new NodeList();
                    item = item.Children.FilterByNameOrIndexOrType(type[i]).FirstOrDefault();
                }
                if (item == null)
                    return new NodeList();
                return item.Children.FilterByNameOrIndexOrType(type[type.Length - 1]);
            }
        }

        public Node GetChild(params NodeType[] type) => GetChildren(type).FirstOrDefault();

        public Node GetChild(params SearchNode[] sn)
        {
            if (sn.Length == 0)
                return null;
            if (sn.Length == 1)
                return Children.FilterByTypeAndNameOrIndex(sn[0]).FirstOrDefault();
            if (sn.Length == 2)
            {
                var item = Children.FilterByTypeAndNameOrIndex(sn[0]).FirstOrDefault();
                return item?.Children.FilterByTypeAndNameOrIndex(sn[1]).FirstOrDefault();
            }
            else
            {
                var item = Children.FilterByTypeAndNameOrIndex(sn[0]).FirstOrDefault();
                for (int i = 1; i < (sn.Length - 1); i++)
                {
                    if (item == null)
                        return null;
                    item = item.Children.FilterByTypeAndNameOrIndex(sn[i]).FirstOrDefault();
                }
                return item?.Children.FilterByTypeAndNameOrIndex(sn[sn.Length - 1]).FirstOrDefault();
            }
        }

        public static SearchNodeList ParseSearchExpression(string searchExpression, out bool success)
        {
            success = true;
#if !DEBUG
         try
         {
#endif
            return new SearchExpressionParser(searchExpression).Parse();
#if !DEBUG
         }
         catch
         {
            success = false;
            return null;
         }
#endif
        }

        public static SearchNodeList ParseSearchExpression(string searchExpression) =>
            new SearchExpressionParser(searchExpression).Parse();

        public Node GetChild(string searchExpression) =>
            GetChild(new SearchExpressionParser(searchExpression).Parse().ToArray());

        public Node GetNextSibling() =>
            Parent?.Children.GetNextSibling(this);

        public Node GetPreviousSibling() =>
            Parent?.Children.GetPreviousSibling(this);

        private static Trivia.TriviaTypes GetTriviaType(SyntaxTrivia t)
        {
            if (t.Kind() == SyntaxKind.RegionDirectiveTrivia)
                return Trivia.TriviaTypes.RegionDirectiveTriviaSyntaxType;
            else if (t.Kind() == SyntaxKind.SingleLineCommentTrivia)
                return Trivia.TriviaTypes.SingleLineCommentTriviaType;
            else if (t.Kind() == SyntaxKind.EndRegionDirectiveTrivia)
                return Trivia.TriviaTypes.EndRegionDirectiveTriviaType;
            else if (t.Kind() == SyntaxKind.MultiLineCommentTrivia)
                return Trivia.TriviaTypes.MultiLineCommentTriviaType;
            else if (t.Kind() == SyntaxKind.SingleLineDocumentationCommentTrivia)
                return Trivia.TriviaTypes.SingleLineDocumentationCommentTriviaType;
            else if (t.Kind() == SyntaxKind.IfDirectiveTrivia)
                return Trivia.TriviaTypes.IfDirectiveTriviaType;
            else if (t.Kind() == SyntaxKind.DisabledTextTrivia)
                return Trivia.TriviaTypes.DisabledTextTriviaType;
            else if (t.Kind() == SyntaxKind.ElseDirectiveTrivia)
                return Trivia.TriviaTypes.ElseDirectiveTriviaType;
            else if (t.Kind() == SyntaxKind.PragmaChecksumDirectiveTrivia)
                return Trivia.TriviaTypes.PragmaChecksumDirectiveTriviaType;
            else if (t.Kind() == SyntaxKind.LineDirectiveTrivia)
                return Trivia.TriviaTypes.LineDirectiveTriviaType;
            else
            {
#if DEBUG
                Console.WriteLine(t.GetType().Name);
                var code = t.ToString().Length > 50 ? t.ToString().Substring(0, 50) : t.ToString();
                Console.WriteLine(code);
                throw new Exception(t.Kind().ToString());
#else
            return Trivia.TriviaTypes.UnknownTriviaSyntaxType;
#endif
            }
        }

        public override string ToString() => string.IsNullOrEmpty(Name) ? NodeType.ToString() : $"{NodeType}[{Name}]";
    }
}