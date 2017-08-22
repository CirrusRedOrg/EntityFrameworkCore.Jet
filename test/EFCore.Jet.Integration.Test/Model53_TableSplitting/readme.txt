This is a solution for "Complex type cannot contain navigation properties"
https://msdn.microsoft.com/en-us/library/bb738472.aspx
http://stackoverflow.com/questions/42403179/how-do-i-have-class-properties-with-navigational-props-as-entity-properties-c

With this model, also removing the navigation property from Address to Person (with complex type you don't have this feature), you need to have an Address class for each entity that use it (for example one for Person like in this case, one for Customer and so on).
