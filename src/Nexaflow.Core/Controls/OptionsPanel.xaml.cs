using System.Windows;
using System.Windows.Controls;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Controls;

/// <summary>Selects the editor DataTemplate based on <see cref="PropertyEditorKind"/>.</summary>
public sealed class PropertyEditorSelector : DataTemplateSelector
{
    public DataTemplate? TextBoxTemplate      { get; set; }
    public DataTemplate? FolderPathTemplate   { get; set; }
    public DataTemplate? FilePathTemplate     { get; set; }
    public DataTemplate? EnumComboBoxTemplate { get; set; }
    public DataTemplate? ListComboBoxTemplate { get; set; }
    public DataTemplate? ToggleTemplate       { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is PropertyEditViewModel vm
            ? vm.EditorKind switch
            {
                PropertyEditorKind.FolderPath    => FolderPathTemplate,
                PropertyEditorKind.FilePath      => FilePathTemplate,
                PropertyEditorKind.EnumComboBox  => EnumComboBoxTemplate,
                PropertyEditorKind.ListComboBox  => ListComboBoxTemplate,
                PropertyEditorKind.Toggle        => ToggleTemplate,
                _                                => TextBoxTemplate,
            }
            : base.SelectTemplate(item, container);
}

public partial class OptionsPanel : UserControl
{
    public OptionsPanel() => InitializeComponent();
}
