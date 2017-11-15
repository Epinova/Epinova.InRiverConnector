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

        public List<XElement> Nodes { get; set; }
        public List<XElement> Entries { get; set; }
        public List<XElement> Relations { get; set; }
        public List<XElement> Associations { get; set; }

        public void AddRelation(string relationName)
        {
            _addedRelations.Add(relationName);
        }

        public bool HasRelation(string relationName)
        {
            return _addedRelations.Contains(relationName);
        }

        public void AddEntity(string entityCode)
        {
            _addedEntities.Add(entityCode);
        }

        public bool HasEntry(string relationName)
        {
            return _addedEntities.Contains(relationName);
        }

        public void AddNode(string nodeCode)
        {
            _addedNodes.Add(nodeCode);
        }

        public bool HasNode(string nodeCode)
        {
            return _addedNodes.Contains(nodeCode);
        }

        public void AddAssociation(string associationName)
        {
            _addedAssociations.Add(associationName);
        }

        public bool HasAssociation(string associationName)
        {
            return _addedAssociations.Contains(associationName);
        }
    }
}