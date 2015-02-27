using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Refactoring
{
    [ExportCodeFixProvider("CodeCrackerIntroduceFieldFromConstructorCodeFixProvider", LanguageNames.CSharp), Shared]
    public class IntroduceFieldFromConstructorCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId.IntroduceFieldFromConstructor.ToDiagnosticId());
        public readonly static string MessageFormat = "Introduce field: {0} from constructor.";

        public sealed override FixAllProvider GetFixAllProvider() => IntroduceFieldFromConstructorCodeFixAllProvider.Instance;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var parameter = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<ParameterSyntax>().First();
            var declaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().First();
            context.RegisterCodeFix(CodeAction.Create(string.Format(MessageFormat, parameter), c => IntroduceFieldFromConstructorDocumentAsync(context.Document, declaration, parameter, c)), diagnostic);
        }
        public async Task<Document> IntroduceFieldFromConstructorDocumentAsync(Document document, ConstructorDeclarationSyntax constructorStatement, ParameterSyntax parameter, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newRoot = IntroduceFieldFromConstructorAsync(root, constructorStatement, parameter);
            var newDocument = document.WithSyntaxRoot(newRoot);
            return document.WithSyntaxRoot(newRoot);
        }

        public static SyntaxNode IntroduceFieldFromConstructorAsync(SyntaxNode root, ConstructorDeclarationSyntax constructorStatement, ParameterSyntax parameter)
        {
            var oldClass = constructorStatement.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            var newClass = oldClass;
            var fieldName = parameter.Identifier.ValueText;
            var fieldType = parameter.Type;
            var members = ExtractMembersFromClass(oldClass.Members);

            if (!members.Any(p => p.Key == fieldName && p.Value == fieldType))
            {
                var identifierPostFix = 0;
                while (members.Any(p => p.Key == fieldName))
                    fieldName = fieldName+ ++identifierPostFix;
                var newField = SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(parameter.Type)
                                    .WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(fieldName)))))
                                    .WithModifiers(SyntaxFactory.TokenList(new[] { SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword) }))
                                    .WithAdditionalAnnotations(Formatter.Annotation);
                newClass = newClass.WithMembers(newClass.Members.Insert(0, newField)).WithoutAnnotations(Formatter.Annotation);
            }
            var assignmentField = SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                               SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ThisExpression(),
                                               SyntaxFactory.IdentifierName(fieldName)), SyntaxFactory.IdentifierName(parameter.Identifier.ValueText)));
            var newConstructor = constructorStatement.WithBody(constructorStatement.Body.AddStatements(assignmentField));
            newClass = newClass.ReplaceNode(newClass.DescendantNodes().OfType<ConstructorDeclarationSyntax>().First(), newConstructor);
            var newRoot = root.ReplaceNode(oldClass, newClass);
            return newRoot;
        }

        private static Dictionary<string,TypeSyntax> ExtractMembersFromClass(SyntaxList<MemberDeclarationSyntax> classMembers)
        {
            var members = new Dictionary<string, TypeSyntax>();
            foreach (var m in classMembers)
            {
                var name = "";
                if (m.GetType().Name == "MethodDeclarationSyntax")
                {
                    var eve = m as MethodDeclarationSyntax;
                    name = eve.Identifier.Text;
                }
                if (m.GetType().Name == "EventDeclarationSyntax")
                {
                    var eve = m as EventDeclarationSyntax;
                    name = eve.Identifier.Text;
                   
                }
                if (m.GetType().Name == "EventFieldDeclarationSyntax")
                {
                    var eve = m as EventFieldDeclarationSyntax;
                    foreach (var v in eve.Declaration.Variables)
                    {
                        members.Add(v.Identifier.Text,eve.Declaration.Type);
                    }
                }
                if (m.GetType().Name == "FieldDeclarationSyntax")
                {
                    var eve = m as FieldDeclarationSyntax;
                    foreach (var v in eve.Declaration.Variables)
                    {
                        members.Add(v.Identifier.Text, eve.Declaration.Type);
                    }
                }
                if (m.GetType().Name == "PropertyDeclarationSyntax")
                {
                    var eve = m as PropertyDeclarationSyntax;
                    name = eve.Identifier.Text;
                }
                if (name != "")
                {
                    members.Add(name,null);
                }
            }
            return members;
        }
    }
}