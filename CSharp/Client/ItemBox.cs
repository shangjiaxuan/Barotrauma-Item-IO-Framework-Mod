using Barotrauma.Items.Components;
using Barotrauma;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System;
using System.ComponentModel;

namespace BaroMod_sjx {
	partial class ItemBoxClient
	{
		static Dictionary<Type, ItemComponent> get_componentsByType(Item item)
		{
			return (AccessTools.Field(typeof(Item), "componentsByType").GetValue(item)! as Dictionary<Type, ItemComponent>)!;
		}

		[HarmonyPatch(typeof(Inventory), nameof(Inventory.DrawSlot))]
		class Patch_DrawSlot {
			public class context {
				public SpriteBatch spriteBatch;
				
				public Inventory inventory;
				public Sprite? indicatorSprite;
				public Sprite? emptyIndicatorSprite;
				public Sprite? itemSprite;
				public Rectangle conditionIndicatorArea;

				public int max_value;
				public int cur_value;
				public Vector2 sprite_pos;
				public float sprite_scale;
				public float rotation;
				public Color spriteColor;
				public context(SpriteBatch sb, Inventory inv, Sprite? full, Sprite? empty,  Sprite? item, Rectangle area,int max, int cur, Vector2 sp, float ss, float rot, Color sc) {
					spriteBatch = sb;
					inventory = inv;
					indicatorSprite = full;
					emptyIndicatorSprite = empty;
					itemSprite = item;
					conditionIndicatorArea = area;
					max_value = max;
					cur_value = cur;
					sprite_pos = sp;
					sprite_scale = ss;
					rotation = rot;
					spriteColor = sc;
				}
			}

			static private void Invoke_DrawItemStateIndicator(
				SpriteBatch spriteBatch, Inventory inventory,
				Sprite indicatorSprite, Sprite emptyIndicatorSprite, Rectangle containedIndicatorArea, float containedState,
				bool pulsate = false) {
				AccessTools.Method(typeof(Inventory), "DrawItemStateIndicator")!
					.Invoke(null, new object[] { spriteBatch, inventory , indicatorSprite , emptyIndicatorSprite , containedIndicatorArea , containedState , pulsate });
			}
			
			private static Sprite? GetTargetSprite(ConditionStorage conditionStorage, Inventory iv) {
				Inventory.ItemSlot target_slot;
				{
					Inventory.ItemSlot[] slots = (AccessTools.Field(typeof(Inventory), "slots").GetValue(iv)! as Inventory.ItemSlot[])!;
					if (conditionStorage.slotIndex >= slots.Length)
					{
						DebugConsole.LogError($"ConditionStorage of {(iv.Owner as Item)!.Prefab.Identifier} specified index {conditionStorage.slotIndex} out of {slots.Length}!");
						return null;
					}
					target_slot = slots[conditionStorage.slotIndex];
				}
				if (target_slot.Any())
				{
					Item i = target_slot.First();
					return i.Prefab.InventoryIcon ?? i.Sprite;
				}
				else
				{
					return null;
				}
			}

			public static bool Prefix(out context? __state, 
				SpriteBatch spriteBatch, Inventory inventory, VisualSlot slot, Item item, int slotIndex) {
				if (inventory != null && item != null && get_componentsByType(item).TryGetValue(typeof(ConditionStorage), out ItemComponent? comp)) {
					ConditionStorage conditionStorage = (comp as ConditionStorage)!;
					if (!conditionStorage.showIcon && !conditionStorage.showCount) {
						__state = null;
						return true;
					}


					Rectangle rect = slot.Rect;
					rect.Location += slot.DrawOffset.ToPoint();

					if (slot.HighlightColor.A > 0)
					{
						float inflateAmount = (slot.HighlightColor.A / 255.0f) * slot.HighlightScaleUpAmount * 0.5f;
						rect.Inflate(rect.Width * inflateAmount, rect.Height * inflateAmount);
					}

					var itemContainer = item.GetComponent<ItemContainer>();

					Sprite? indicatorSprite;
					Sprite? emptyIndicatorSprite;
					Rectangle conditionIndicatorArea;
					if (conditionStorage.showCount)
					{


						if (itemContainer is null)
						{
							DebugConsole.LogError($"Item {item.Prefab.Identifier} has ConditionStorage but no ItemContainer!");
							__state = null;
							return true;
						}
						if (itemContainer.InventoryTopSprite != null || itemContainer.InventoryBottomSprite != null)
						{
							__state = null;
							return true;
						}
						int dir = slot.SubInventoryDir;

						if (itemContainer.ShowContainedStateIndicator)
						{
							conditionIndicatorArea = new Rectangle(rect.X, rect.Bottom - (int)(10 * GUI.Scale), rect.Width, (int)(10 * GUI.Scale));
						}
						else
						{
							conditionIndicatorArea = new Rectangle(
								rect.X, dir < 0 ? rect.Bottom + HUDLayoutSettings.Padding / 2 : rect.Y - HUDLayoutSettings.Padding / 2 - Inventory.ContainedIndicatorHeight,
								rect.Width, Inventory.ContainedIndicatorHeight);
							conditionIndicatorArea.Inflate(-4, 0);
						}


						GUIComponentStyle indicatorStyle = GUIStyle.GetComponentStyle("ContainedStateIndicator.Default")!;
						indicatorSprite = indicatorStyle.GetDefaultSprite();
						emptyIndicatorSprite = indicatorStyle.GetSprite(GUIComponent.ComponentState.Hover);
					}
					else {
						indicatorSprite = null;
						emptyIndicatorSprite = null;
						conditionIndicatorArea = new Rectangle();
					}

					Vector2 itemPos;
					float scale;
					float rotation;
					Sprite? item_sprite;
					Color spriteColor;

					if (conditionStorage.showIcon)
					{
						item_sprite = GetTargetSprite(conditionStorage, itemContainer.Inventory!);
						if (item_sprite != null)
						{
							scale = Math.Min(Math.Min((rect.Width - 10) / item_sprite.size.X, (rect.Height - 10) / item_sprite.size.Y), 2.0f);
							itemPos = rect.Center.ToVector2();
							if (itemPos.Y > GameMain.GraphicsHeight)
							{
								itemPos.Y -= Math.Min(
									(itemPos.Y + item_sprite.size.Y / 2 * scale) - GameMain.GraphicsHeight,
									(itemPos.Y - item_sprite.size.Y / 2 * scale) - rect.Y);
							}

							rotation = 0.0f;
							if (slot.HighlightColor.A > 0)
							{
								rotation = (float)Math.Sin(slot.HighlightTimer * MathHelper.TwoPi) * slot.HighlightTimer * 0.3f;
							}

							spriteColor = item_sprite == item.Sprite ? item.GetSpriteColor() : item.GetInventoryIconColor();
						}
						else
						{
							scale = 1.0f;
							rotation = 0.0f;
							spriteColor = Color.White;
						}
					}
					else {
						item_sprite = null;
						scale = 1.0f;
						rotation = 0.0f;
						spriteColor = Color.White;
					}
					Vector2 center = slot.Rect.Center.ToVector2() + (new Vector2(conditionStorage.iconShiftX, conditionStorage.iconShiftY))*slot.Rect.Size.ToVector2()*0.5f;
					__state = new context(spriteBatch, inventory, indicatorSprite, emptyIndicatorSprite, item_sprite,
						conditionIndicatorArea, conditionStorage.maxItemCount, conditionStorage.currentItemCount, center, 
							scale*conditionStorage.iconScale, rotation, spriteColor);
				}
				else {
					__state = null;
				}
				return true;
			}
			public static void Postfix(context? __state) {
				if (__state != null) {
					__state.itemSprite?.Draw(__state.spriteBatch, __state.sprite_pos, __state.spriteColor, __state.rotation, __state.sprite_scale);
					if (__state.indicatorSprite != null && __state.emptyIndicatorSprite != null) {
						Invoke_DrawItemStateIndicator(__state.spriteBatch, __state.inventory, __state.indicatorSprite, __state.emptyIndicatorSprite, __state.conditionIndicatorArea,
							__state.cur_value / (float)__state.max_value);
						string info_text = $"{__state.cur_value}/{__state.max_value}";
						float text_scale = 0.75f;
						Vector2 info_size = GUIStyle.SmallFont.MeasureString(info_text) * text_scale;
						GUIStyle.SmallFont.DrawString(__state.spriteBatch, info_text, __state.conditionIndicatorArea.Center.ToVector2() - (info_size * 0.5f), Color.White, 0.0f, Vector2.Zero, text_scale, SpriteEffects.None, 0.0f);
					}
				}
			}
		}
	}
}
