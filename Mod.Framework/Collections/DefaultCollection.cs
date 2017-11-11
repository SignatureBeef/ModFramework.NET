namespace Mod.Framework.Collections
{
	public class DefaultCollection<TItem> : IDefaultCollection<TItem>
	{
		protected TItem[,] _items;

		public virtual TItem this[int x, int y]
		{
			get
			{
				return _items[x, y];
			}
			set
			{
				_items[x, y] = value;
			}
		}

		public void Initialise(int width, int height)
		{
			_items = new TItem[width, height];
		}
	}

	// this will not be needed when we clone or generate in the target assembly
	// this is meerly used to force DefaultCollection to have the final attributes (etc) so that the target assembly can use interfaces correctly
	interface IDefaultCollection<TItem>
	{
		TItem this[int x, int y]
		{
			get;
			set;
		}

		void Initialise(int x, int y);
	}
}
