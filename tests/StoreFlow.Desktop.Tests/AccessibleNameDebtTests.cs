using System.IO;
using System.Xml.Linq;
using Shouldly;

namespace StoreFlow.Desktop.Tests;

/// <summary>Tracks the source-level accessible-name debt on identified controls.</summary>
public sealed class AccessibleNameDebtTests
{
    [Fact]
    public void Completed_screens_keep_their_localized_names()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreFlow.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        string views = Path.Combine(root.FullName, "src", "StoreFlow.Desktop", "Views");
        (string File, string Id, string Binding)[] expected =
        [
            ("AuditView.xaml", "Audit.Search", "SearchPlaceholder"),
            ("AuditView.xaml", "Audit.EntityType", "EntityTypeLabel"),
            ("AuditView.xaml", "Audit.From", "FromLabel"),
            ("AuditView.xaml", "Audit.To", "ToLabel"),
            ("AuditView.xaml", "Audit.Actor", "ActorLabel"),
            ("AuditView.xaml", "Audit.Grid", "Title"),
            ("CategoriesView.xaml", "Categories.Grid", "Title"),
            ("UnitsView.xaml", "Units.Grid", "Title"),
            ("SuppliersView.xaml", "Suppliers.Search", "SearchPlaceholder"),
            ("SuppliersView.xaml", "Suppliers.Grid", "Title"),
            ("SalesView.xaml", "SalesSearch", "SearchPlaceholder"),
            ("SalesView.xaml", "SalesFrom", "FromLabel"),
            ("SalesView.xaml", "SalesTo", "ToLabel"),
            ("SalesView.xaml", "SalesCashier", "CashierLabel"),
            ("SalesView.xaml", "SalesRegister", "Title"),
            ("PurchasesView.xaml", "Purchases.Search", "SearchPlaceholder"),
            ("PurchasesView.xaml", "Purchases.From", "FromLabel"),
            ("PurchasesView.xaml", "Purchases.To", "ToLabel"),
            ("PurchasesView.xaml", "Purchases.Supplier", "SupplierFilterLabel"),
            ("PurchasesView.xaml", "Purchases.Grid", "Title"),
            ("ApprovalsView.xaml", "Approvals.Grid", "Title"),
        ];
        foreach ((string file, string id, string binding) in expected)
        {
            XElement control = XDocument.Load(Path.Combine(views, file)).Descendants()
                .Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == id);
            ((string?)control.Attribute("AutomationProperties.Name")).ShouldBe($"{{Binding {binding}}}");
        }
    }

    [Fact]
    public void Identified_interactive_controls_without_a_declared_or_content_name_do_not_increase()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoreFlow.sln")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull();
        string views = Path.Combine(root.FullName, "src", "StoreFlow.Desktop");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string[] interactive = ["TextBox", "PasswordBox", "ComboBox", "DatePicker", "DataGrid", "ListBox", "ListView", "Button", "CheckBox", "RadioButton", "ToggleButton"];
        string[] namingAttributes = ["AutomationProperties.Name", "AutomationProperties.LabeledBy", "Content", "Header"];
        string[] unnamed = Directory.GetFiles(views, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.Namespace == presentation && interactive.Contains(element.Name.LocalName, StringComparer.Ordinal))
                .Where(element => element.Attribute("AutomationProperties.AutomationId") is not null)
                .Where(element => !namingAttributes.Any(name => !string.IsNullOrWhiteSpace((string?)element.Attribute(name))))
                .Select(element => $"{Path.GetFileName(path)}: {(string?)element.Attribute("AutomationProperties.AutomationId")}"))
            .ToArray();

        unnamed.Length.ShouldBeLessThanOrEqualTo(88, string.Join(Environment.NewLine, unnamed));
    }
}
