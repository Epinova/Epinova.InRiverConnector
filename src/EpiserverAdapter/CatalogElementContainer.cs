using System.Collections.Generic;
using System.Xml.Linq;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class CatalogElementContainer
    {
        public CatalogElementContainer()
        {
            Nodes = new List<XElement>();
            Entries = new List<XElement>();
            Relations = new List<XElement>();
            Associations = new List<XElement>();

            _addedEntities = new List<string>();
            _addedNodes = new List<string>();
            _addedRelations = new List<string>();
            _addedAssociations = new List<string>();
        }

        private readonly List<string> _addedEntities;
        private readonly List<string> _addedNodes;
        private readonly List<string> _addedRelations;
        private readonly List<string> _addedAssociations;

        public List<XElement> Nodes { get; }
        public List<XElement> Entries { get; }
        public List<XElement> Relations { get; set; }
        public List<XElement> Associations { get; set; }

        public void AddRelation(XElement relation, string relationName)
        {
            Relations.Add(relation);
            _addedRelations.Add(relationName);
        }

        public bool HasRelation(string relationName)
        {
            return _addedRelations.Contains(relationName);
        }

        public void AddEntry(XElement entry, string entryCode)
        {
            Entries.Add(entry);
            _addedEntities.Add(entryCode);
        }

        public bool HasEntry(string entryCode)
        {
            return _addedEntities.Contains(entryCode);
        }

        public void AddNode(XElement node, string nodeCode)
        {
            Nodes.Add(node);
            _addedNodes.Add(nodeCode);
        }

        public bool HasNode(string nodeCode)
        {
            return _addedNodes.Contains(nodeCode);
        }

        public void AddAssociation(XElement association, string associationName)
        {
            Associations.Add(association);
            _addedAssociations.Add(associationName);
        }

        public bool HasAssociation(string associationName)
        {
            return _addedAssociations.Contains(associationName);
        }
    }
}