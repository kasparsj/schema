using System;
using System.Collections.Generic;
using System.Reflection;

/***
  Allowed primitive types:
    "string"
    "number"
    "boolean"
    "int8"
    "uint8"
    "int16"
    "uint16"
    "int32"
    "uint32"
    "int64"
    "uint64"
    "float32"
    "float64"
       
  Allowed reference types:   
    "ref"
    "array"
    "map"
***/

namespace Colyseus.Schema
{
  [AttributeUsage(AttributeTargets.Field)]
  public class Type : Attribute
  {

    public string FieldType;
    public System.Type ChildType;

    public Type(string type, System.Type childType = null)
    {
      FieldType = type;
      ChildType = childType;
    }
  }

  public class Iterator { 
    public int Offset = 0;
  }

  public enum SPEC: byte
  {
    END_OF_STRUCTURE = 0xc1, // (msgpack spec: never used)
    NIL = 0xc0,
    INDEX_CHANGE = 0xd4,
  }

  public class DataChange
  {
    public string Field;
    public object Value;
    public object PreviousValue;
  }

  public class OnChangeEventArgs : EventArgs
  {
    public List<DataChange> Changes;
    public OnChangeEventArgs(List<DataChange> changes)
    {
      Changes = changes;
    }
  }

  public class Schema
  {
    protected Dictionary<int, string> fieldsByIndex = new Dictionary<int, string>();
    protected Dictionary<string, string> fieldTypes = new Dictionary<string, string>();
    protected Dictionary<string, System.Type> fieldChildTypes = new Dictionary<string, System.Type>();

    public event EventHandler<OnChangeEventArgs> OnChange;
    public event EventHandler OnRemove;

    public Schema()
    {
      int index = 0;

      FieldInfo[] fields = GetType().GetFields();
      foreach (FieldInfo field in fields)
      {
        Type t = field.GetCustomAttribute<Type>();
        if (t != null)
        {
          fieldsByIndex.Add(index++, field.Name);
          fieldTypes.Add(field.Name, t.FieldType);
          if (t.FieldType == "ref" || t.FieldType == "array" || t.FieldType == "map")
          {
            fieldChildTypes.Add(field.Name, t.ChildType);
          }
        }
      }
    }

    /* allow to retrieve property values by its string name */   
    public object this[string propertyName]
    {
      get { 
        return GetType().GetField(propertyName).GetValue(this); 
      }
      set {
        var field = GetType().GetField(propertyName);
        field.SetValue(this, Convert.ChangeType(value, field.FieldType)); 
      }
    }

    public void Decode(byte[] bytes, Iterator it = null)
    {
      if (it == null) { it = new Iterator(); }

      var t = GetType();

      var changes = new List<DataChange>();
      var totalBytes = bytes.Length;

      while (it.Offset < totalBytes)
      {
        var index = bytes[it.Offset++];

        if (index == (byte) SPEC.END_OF_STRUCTURE)
        {
          break;
        }

        var field = fieldsByIndex[index];
        var fieldType = fieldTypes[field];
        object value = null;

        object change = null;
        bool hasChange = false;

        if (fieldType == "ref")
        {
          // child schema type
          if (Decoder.GetInstance().NilCheck(bytes, it))
          {
            it.Offset++;
            value = null;

          }
          else
          {
            System.Type childType = fieldChildTypes[field];
            value = this[field] ?? Activator.CreateInstance(childType);
            (value as Schema).Decode(bytes, it);
          }

          hasChange = true;
        }
        else if (fieldType == "array")
        {
          // array type
        }
        else if (fieldType == "map")
        {
          // map type
        }
        else
        {
          // primitive type
          value = Decoder.GetInstance().DecodePrimitiveType(fieldType, bytes, it);
          hasChange = true;
        }

        if (hasChange)
        {
          changes.Add(new DataChange
          {
            Field = field,
            Value = (change != null) ? change : value,
            PreviousValue = this[field]
          });
        }

        this[field] = value;
      }

      if (changes.Count > 0 && OnChange != null)
      {
        // TODO: provide 'changes' list to onChange event.
        OnChange.Invoke(this, new OnChangeEventArgs(changes));
      }
    }
  }
}