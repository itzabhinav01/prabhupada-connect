using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.UI.Helpers
{
    public class NavMenuItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? BookTemplate { get; set; }
        public DataTemplate? FolderTemplate { get; set; }
        public DataTemplate? HeaderTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is BookNode node)
            {
                if (node.IsFolder) return FolderTemplate ?? BookTemplate;
                if (node.IsHeader) return HeaderTemplate;
            }
            return BookTemplate;
        }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            if (item is BookNode node)
            {
                if (node.IsFolder) return FolderTemplate ?? BookTemplate;
                if (node.IsHeader) return HeaderTemplate;
            }
            return BookTemplate;
        }
    }
}
