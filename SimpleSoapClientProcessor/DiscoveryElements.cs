using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Schema;

namespace SimpleSoapClientProcessor
{
    public abstract class DiscoveryElementsCollection<T> : ICollection<T>
    {
        private readonly List<T> elements = new List<T>();

        public DiscoveryElementsCollection() { }

        public DiscoveryElementsCollection(T item)
        {
            this.Add(item);
        }

        public DiscoveryElementsCollection(IEnumerable<T> items)
        {
            this.AddRange(items);
        }

        public int Count => elements.Count;

        public bool IsReadOnly => false;

        public virtual T this[int index]
        {
            get
            {
                return elements[index];
            }
        }

        public void Add(T item)
        {
            elements.Add(item);
        }

        public void AddRange(IEnumerable<T> items)
        {
            elements.AddRange(items);
        }

        public void Clear()
        {
            elements.Clear();
        }

        public bool Contains(T item)
        {
            return elements.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            elements.CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return elements.GetEnumerator();
        }

        public bool Remove(T item)
        {
            return elements.Remove(item);
        }

        public T Find(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException("match");
            }

            return elements.Find(match);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return elements.GetEnumerator();
        }
    }

    public class ElementsCollection : DiscoveryElementsCollection<XmlSchemaElement>
    {
        
        public ElementsCollection() { }

        public ElementsCollection(XmlSchemaElement item)
        {
            base.Add(item);
        }

        public ElementsCollection(List<XmlSchemaElement> items)
        {
            base.AddRange(items);
        }

        public XmlSchemaElement this[string index]
        {
            get
            {
                return base.Find(e => e.Name == index);
            }
        }

        public override XmlSchemaElement this[int index]
        {
            get
            {
                return base[index];
            }
        }
    }

    public class DiscoveryElements
    {
        private readonly Dictionary<string, List<XmlSchemaObject>> items = new Dictionary<string, List<XmlSchemaObject>>();

        public Dictionary<string, List<XmlSchemaObject>> Items
        {
            get
            {
                return items;
            }
        }

        public ElementsCollection Elements
        {
            get
            {
                var elementsEnumerable = this.items.Values.SelectMany(e => e);
                var elements = elementsEnumerable.Where(e => e is XmlSchemaElement);
                var elementsList = elements.Cast<XmlSchemaElement>().ToList();
                return new ElementsCollection(elementsList);
            }
        }

        public List<XmlSchemaComplexType> ComplexTypes
        {
            get
            {
                var elementsEnumerable = this.items.Values.SelectMany(e => e);
                var elements = elementsEnumerable.Where(e => e is XmlSchemaComplexType);
                return elements.Cast<XmlSchemaComplexType>().ToList();
            }
        }

        public List<XmlSchemaSimpleType> SimpleTypes
        {
            get
            {
                var elementsEnumerable = this.items.Values.SelectMany(e => e);
                var elements = elementsEnumerable.Where(e => e is XmlSchemaSimpleType);
                return elements.Cast<XmlSchemaSimpleType>().ToList();
            }
        }

        public List<XmlSchemaObject> this[string index]
        {
            get
            {
                if (!items.ContainsKey(index)) { return null; }
                return items[index];
            }
        }

        public DiscoveryElements()
        {

        }

        public bool ContainsKey(string key)
        {
            return items.ContainsKey(key);
        }

        public void Add(XmlSchemaObject schema)
        {
            if (schema is XmlSchemaElement xmlSchemaElement)
            {
                if (!items.ContainsKey(xmlSchemaElement.Name))
                {
                    items.Add(xmlSchemaElement.Name, new List<XmlSchemaObject>());
                }

                items[xmlSchemaElement.Name].Add(schema);
                return;
            }

            if (schema is XmlSchemaComplexType xmlSchemaComplexType)
            {
                if (!items.ContainsKey(xmlSchemaComplexType.Name))
                {
                    items.Add(xmlSchemaComplexType.Name, new List<XmlSchemaObject>());
                }

                items[xmlSchemaComplexType.Name].Add(schema);
                return;
            }

            if (schema is XmlSchemaSimpleType xmlSchemaSimpleType)
            {
                if (!items.ContainsKey(xmlSchemaSimpleType.Name))
                {
                    items.Add(xmlSchemaSimpleType.Name, new List<XmlSchemaObject>());
                }

                items[xmlSchemaSimpleType.Name].Add(schema);
                return;
            }

            throw new ArgumentException("Provided schema not of type XmlSchemaComplexType or XmlSchemaElement", "schema");
        }
    }
}
