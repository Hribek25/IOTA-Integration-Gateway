using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IOTAGears.EntityModels
{
    public class NodeTree
    {
        public string Name { get; set; }
        public List<NodeTree> Children { get; } = new List<NodeTree>();

        public bool HasChildren() => Children.Count > 0;

        public NodeTree(string Name)
        {
            this.Name = Name;
        }

        public NodeTree GetChild(string name)
        {
            return (from i in this.Children where i.Name == name select i).FirstOrDefault();
        }

    }
}
