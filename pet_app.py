import random
import sys
import tkinter as tk
from tkinter import messagebox
from pathlib import Path


APP_NAME = "金丝猴桌面宠物"
SPRITE_PATH = Path(__file__).resolve().parent / "assets" / "sprites" / "girlfriend_cherry_front.png"
TRANSPARENT_COLOR = "#ff00ff"


class DesktopPet:
    def __init__(self) -> None:
        self.root = tk.Tk()
        self.root.title(APP_NAME)
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)
        self.root.configure(bg=TRANSPARENT_COLOR)

        try:
            self.root.wm_attributes("-transparentcolor", TRANSPARENT_COLOR)
        except tk.TclError:
            pass

        if not SPRITE_PATH.exists():
            raise FileNotFoundError(f"Missing sprite: {SPRITE_PATH}")

        self.pet_image = tk.PhotoImage(file=str(SPRITE_PATH))
        self.width = self.pet_image.width()
        self.height = self.pet_image.height()
        self.drag_offset_x = 0
        self.drag_offset_y = 0
        self.dragging = False
        self.idle_tick = 0
        self.hearts: list[int] = []

        self.canvas = tk.Canvas(
            self.root,
            width=self.width,
            height=self.height + 34,
            highlightthickness=0,
            bg=TRANSPARENT_COLOR,
        )
        self.canvas.pack()

        self.pet_item = self.canvas.create_image(
            self.width // 2,
            self.height // 2 + 14,
            image=self.pet_image,
        )

        self.menu = tk.Menu(self.root, tearoff=False)
        self.menu.add_command(label="投喂樱桃", command=self.feed)
        self.menu.add_command(label="说悄悄话", command=self.show_love_note)
        self.menu.add_separator()
        self.menu.add_command(label="置顶 / 保持在桌面", command=self.keep_on_top)
        self.menu.add_command(label="退出", command=self.root.destroy)

        self.bind_events()
        self.place_near_bottom_right()
        self.animate_idle()
        self.spawn_random_heart()

    def bind_events(self) -> None:
        self.canvas.bind("<ButtonPress-1>", self.start_drag)
        self.canvas.bind("<B1-Motion>", self.drag)
        self.canvas.bind("<ButtonRelease-1>", self.end_drag)
        self.canvas.bind("<Double-Button-1>", lambda _event: self.show_love_note())
        self.canvas.bind("<Button-3>", self.open_menu)

    def place_near_bottom_right(self) -> None:
        screen_w = self.root.winfo_screenwidth()
        screen_h = self.root.winfo_screenheight()
        x = max(0, screen_w - self.width - 80)
        y = max(0, screen_h - self.height - 120)
        self.root.geometry(f"{self.width}x{self.height + 34}+{x}+{y}")

    def start_drag(self, event: tk.Event) -> None:
        self.dragging = True
        self.drag_offset_x = event.x
        self.drag_offset_y = event.y

    def drag(self, event: tk.Event) -> None:
        x = self.root.winfo_pointerx() - self.drag_offset_x
        y = self.root.winfo_pointery() - self.drag_offset_y
        self.root.geometry(f"+{x}+{y}")

    def end_drag(self, _event: tk.Event) -> None:
        self.dragging = False
        self.bounce()

    def open_menu(self, event: tk.Event) -> None:
        self.menu.tk_popup(event.x_root, event.y_root)

    def keep_on_top(self) -> None:
        self.root.attributes("-topmost", True)

    def feed(self) -> None:
        self.float_text("樱桃要留给你")
        self.make_hearts(5)

    def show_love_note(self) -> None:
        notes = [
            "今天也喜欢你",
            "这颗樱桃给你",
            "偷偷想你一下",
            "陪你待机中",
        ]
        self.float_text(random.choice(notes))
        self.make_hearts(7)

    def float_text(self, text: str) -> None:
        item = self.canvas.create_text(
            self.width // 2,
            18,
            text=text,
            fill="#6b3a16",
            font=("Microsoft YaHei UI", 14, "bold"),
        )
        self.fade_text(item, 0, 18)

    def fade_text(self, item: int, step: int, y: int) -> None:
        if step >= 45:
            self.canvas.delete(item)
            return
        self.canvas.coords(item, self.width // 2, y - step // 3)
        self.root.after(35, lambda: self.fade_text(item, step + 1, y))

    def make_hearts(self, count: int) -> None:
        for _ in range(count):
            x = random.randint(self.width // 3, self.width * 2 // 3)
            y = random.randint(self.height // 3, self.height // 2)
            heart = self.canvas.create_text(
                x,
                y,
                text="♥",
                fill=random.choice(["#ff6b8a", "#ff8fb0", "#e94b6a"]),
                font=("Segoe UI Symbol", random.randint(14, 22), "bold"),
            )
            self.hearts.append(heart)
            self.float_heart(heart, 0)

    def float_heart(self, item: int, step: int) -> None:
        if step >= 42:
            self.canvas.delete(item)
            if item in self.hearts:
                self.hearts.remove(item)
            return
        self.canvas.move(item, random.choice([-1, 0, 1]), -2)
        self.root.after(45, lambda: self.float_heart(item, step + 1))

    def spawn_random_heart(self) -> None:
        if not self.dragging and random.random() < 0.35:
            self.make_hearts(1)
        self.root.after(random.randint(5000, 9000), self.spawn_random_heart)

    def animate_idle(self) -> None:
        self.idle_tick = (self.idle_tick + 1) % 80
        bob = 2 if self.idle_tick < 40 else 0
        self.canvas.coords(self.pet_item, self.width // 2, self.height // 2 + 14 + bob)
        self.root.after(120, self.animate_idle)

    def bounce(self) -> None:
        steps = [8, 4, 0, -2, 0]

        def run(index: int) -> None:
            if index >= len(steps):
                return
            self.canvas.coords(
                self.pet_item,
                self.width // 2,
                self.height // 2 + 14 + steps[index],
            )
            self.root.after(55, lambda: run(index + 1))

        run(0)

    def run(self) -> None:
        self.root.mainloop()


def main() -> int:
    try:
        DesktopPet().run()
        return 0
    except Exception as exc:
        messagebox.showerror(APP_NAME, str(exc))
        return 1


if __name__ == "__main__":
    sys.exit(main())
