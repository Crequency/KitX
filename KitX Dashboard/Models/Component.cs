namespace KitX_Dashboard.Models
{
    internal class Component
    {
        public Component()
        {

        }

        internal bool CanUpdate { get; set; }

        internal string? Name { get; set; }

        internal string? MD5 { get; set; }

        internal string? SHA1 { get; set; }
    }
}
