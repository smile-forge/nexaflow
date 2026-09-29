# HDF5 viewer

Opens an HDF5 file (`.h5`, `.hdf5` or `.he5`) and shows what is in it: groups, datasets, links and attributes. It only reads, so nothing in the file changes.

---

## Opening a file

- Double-click an HDF5 file on the [File System](help:FileSystem) page, or choose *Open in HDF5 viewer*. A file inside a zip or another archive opens the same way.
- An HDF5 file is also a folder you can browse into. Its groups are folders and its datasets are files: a dataset of numbers, strings of a fixed length or records is a NumPy `.npy` file, and one of variable-length strings is a `.txt` file with one value per line. Copy one out and it loads in NumPy as it is.

## Finding your way

- Open a group in [the Structure tree](locate:Hdf5_Tree) to list its members. A very large group lists a page at a time; choose *Show more…* at the end for the next page.
- Narrow what is listed with [the filter](locate:Hdf5_TreeFilter). It matches names among the objects already loaded, and keeps the groups that lead to them.
- A link that points at nothing, or at another file that is not there, stays in the tree and says why. If the reader cannot decode a member of a group, the listing stops at that member and says so; the members before it can still be opened.

## Reading a dataset

- [The table](locate:Hdf5_Table) shows the selected dataset, reading only the rows on screen, so a dataset of any size opens at once. Row numbers start at 0, as indices do in HDF5.
- A dataset of records shows each field as a column. A dataset of one dimension shows one column of values.
- Data with two or more dimensions lies on the table as rows and columns. Choose which dimension runs down [the rows](locate:Hdf5_SliceRowAxis) and which runs across [the columns](locate:Hdf5_SliceColAxis). Each other dimension gets a slider that holds it at one index.
- Data wider than the table can lay out shows a window of columns; move it across with [the column slider](locate:Hdf5_ColumnOffset).
- If the elements cannot be read, for example because they use a compression filter or a byte order the reader does not support, the table says so. The dataset's details and attributes still show.

## Details and attributes

- [The Details button](locate:Hdf5_DetailsToggle) opens and closes the drawer on the right.
- [Object](locate:Hdf5_ObjectDetails) gives the path, kind, type, shape and maximum shape, where an infinity sign marks a dimension that can still grow. [Storage](locate:Hdf5_StorageDetails) gives the layout, the chunk shape and the fill value.
- [The attributes](locate:Hdf5_Attributes) list each name with its type and value. A long array value shows its first entries and says how many there are. Right-click an attribute to copy its value, its name or the whole row, and narrow the list with [the attribute filter](locate:Hdf5_AttributeFilter).

## Asking the assistant

The assistant sees what the viewer shows: the tree as you have opened it, the selected object with its details and attributes, and the rows of the table on screen. It can list any group, describe any object, read any part of a dataset, and select an object or a slice to show it to you.
