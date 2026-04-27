# ui.py — Interface utilisateur (pygame_gui)
import math
import pygame
import pygame_gui
from physics import densite_atmosphere

# Types disponibles et leurs limites (unité native)
TYPES = {
    "Grenade":          {"min": 1,      "max": 500,     "unite": "g",   "label": "Masse TNT"},
    "Conventionnelle":  {"min": 1,      "max": 50000,   "unite": "kg",  "label": "Masse TNT"},
    "MOAB":             {"min": 1000,   "max": 50000,   "unite": "kg",  "label": "Masse TNT"},
    "Thermobarique":    {"min": 10,     "max": 10000,   "unite": "kg",  "label": "Masse carburant"},
    "Nucléaire":        {"min": 0.001,  "max": 500,     "unite": "kt",  "label": "Puissance"},
    "Neutron":          {"min": 0.001,  "max": 50,      "unite": "kt",  "label": "Puissance"},
    "Mégatonne":        {"min": 100,    "max": 100000,  "unite": "kt",  "label": "Puissance"},
}

SLIDER_MAX = 1000


class UI:
    def __init__(self, manager: pygame_gui.UIManager, rect: pygame.Rect):
        self.manager = manager
        self.rect    = rect
        self._type   = "Grenade"
        self._rho_auto = True

        mx, my = rect.left + 10, rect.top + 30
        lw = rect.width - 20
        hs = 28
        sp = 8

        self.lbl_titre = self._lbl(mx, my, lw, 20, "─── SANDBOX ───")
        my += 28

        self.lbl_type = self._lbl(mx, my, lw, 18, "Type d'explosion :")
        my += 20
        self.dd_type = pygame_gui.elements.UIDropDownMenu(
            options_list=list(TYPES.keys()),
            starting_option="Grenade",
            relative_rect=pygame.Rect(mx, my, lw, 30),
            manager=manager,
        )
        my += 36

        self.lbl_puiss = self._lbl(mx, my, lw, 18, "Masse TNT : --- g")
        my += 20
        self.sl_puiss = pygame_gui.elements.UIHorizontalSlider(
            relative_rect=pygame.Rect(mx, my, lw, hs),
            start_value=400, value_range=(0, SLIDER_MAX), manager=manager)
        my += hs + sp

        self.lbl_alt = self._lbl(mx, my, lw, 18, "Altitude : 0 m")
        my += 20
        self.sl_alt = pygame_gui.elements.UIHorizontalSlider(
            relative_rect=pygame.Rect(mx, my, lw, hs),
            start_value=0, value_range=(0, 5000), manager=manager)
        my += hs + sp

        self.lbl_monde = self._lbl(mx, my, lw, 18, "Taille du monde : 10.0 km")
        my += 20
        self.sl_monde = pygame_gui.elements.UIHorizontalSlider(
            relative_rect=pygame.Rect(mx, my, lw, hs),
            start_value=398, value_range=(0, SLIDER_MAX), manager=manager)
        my += hs + sp

        self.lbl_densite_bat = self._lbl(mx, my, lw, 18, "Densité des bâtiments : 40 / km²")
        my += 20
        self.sl_densite_bat = pygame_gui.elements.UIHorizontalSlider(
            relative_rect=pygame.Rect(mx, my, lw, hs),
            start_value=40, value_range=(5, 250), manager=manager)
        my += hs + sp

        self.lbl_superficie = self._lbl(mx, my, lw, 18, "Superficie des bâtiments : 1.00x")
        my += 20
        self.sl_superficie = pygame_gui.elements.UIHorizontalSlider(
            relative_rect=pygame.Rect(mx, my, lw, hs),
            start_value=100, value_range=(50, 300), manager=manager)
        my += hs + sp

        self.lbl_rho = self._lbl(mx, my, lw, 18,
                                  f"Densité : {densite_atmosphere(0):.4f} kg/m³")
        my += 20
        self.sl_rho = pygame_gui.elements.UIHorizontalSlider(
            relative_rect=pygame.Rect(mx, my, lw, hs),
            start_value=1225, value_range=(200, 1400), manager=manager)
        my += hs + 4
        self.btn_rho = pygame_gui.elements.UIButton(
            relative_rect=pygame.Rect(mx, my, lw, 26),
            text="Densité : AUTO", manager=manager)
        my += 30 + sp

        self.btn_go = pygame_gui.elements.UIButton(
            relative_rect=pygame.Rect(mx, my, lw, 38),
            text="▶  DÉCLENCHER", manager=manager)
        my += 42
        self.btn_reset = pygame_gui.elements.UIButton(
            relative_rect=pygame.Rect(mx, my, lw, 28),
            text="Réinitialiser", manager=manager)
        my += 34

        self.btn_regen = pygame_gui.elements.UIButton(
            relative_rect=pygame.Rect(mx, my, lw, 30),
            text="Régénérer le sandbox", manager=manager)
        my += 36

        # ── Légende couleurs ──────────────────────────────────────────────────
        self.lbl_leg = self._lbl(mx, my, lw, 18, "─── LÉGENDE ───")
        my += 22
        items = [
            ((220, 50,  50), "Zone mortelle  > 100 kPa"),
            ((230, 140, 30), "Zone modérée   35–100 kPa"),
            ((60,  200, 60), "Zone légère    7–35 kPa"),
        ]
        self.legende = []
        for col, txt in items:
            self.legende.append((col, self._lbl(mx + 18, my, lw - 18, 16, txt)))
            my += 20
        self.legende_rects = [(pygame.Rect(mx, my - 20*i - 20, 14, 14), col)
                               for i, (col, _) in enumerate(reversed(self.legende))]

        self.maj_labels()

    def _lbl(self, x, y, w, h, txt):
        return pygame_gui.elements.UILabel(
            relative_rect=pygame.Rect(x, y, w, h),
            text=txt, manager=self.manager)

    def traiter(self, event) -> dict:
        actions = {"go": False, "reset": False, "regen": False, "type_change": None}
        if event.type == pygame_gui.UI_DROP_DOWN_MENU_CHANGED:
            if event.ui_element == self.dd_type:
                self._type = event.text
                actions["type_change"] = event.text
        elif event.type == pygame_gui.UI_BUTTON_PRESSED:
            if event.ui_element == self.btn_go:
                actions["go"] = True
            elif event.ui_element == self.btn_reset:
                actions["reset"] = True
            elif event.ui_element == self.btn_regen:
                actions["regen"] = True
            elif event.ui_element == self.btn_rho:
                self._rho_auto = not self._rho_auto
                self.btn_rho.set_text("Densité : " + ("AUTO" if self._rho_auto else "MANUELLE"))
        return actions

    def maj_labels(self):
        lim = TYPES[self._type]
        v   = self.puissance
        self.lbl_puiss.set_text(f"{lim['label']} : {_fmt(v)} {lim['unite']}")
        alt = int(self.altitude)
        self.lbl_alt.set_text(f"Altitude : {alt} m")
        self.lbl_monde.set_text(f"Taille du monde : {self.taille_monde_m / 1000:.1f} km")
        self.lbl_densite_bat.set_text(f"Densité des bâtiments : {self.densite_batiments_km2:.0f} / km²")
        self.lbl_superficie.set_text(f"Superficie des bâtiments : {self.superficie_batiments:.2f}x")
        rho = densite_atmosphere(alt) if self._rho_auto else self.sl_rho.get_current_value() / 1000
        mode = "auto" if self._rho_auto else "man"
        self.lbl_rho.set_text(f"Densité : {rho:.4f} kg/m³ ({mode})")

    def dessiner_legende(self, surf: pygame.Surface):
        """Dessine les carrés de couleur de la légende (pygame_gui ne gère pas les rectangles colorés)."""
        for rect, col in self.legende_rects:
            r = pygame.Rect(rect.x, rect.y, rect.w, rect.h)
            pygame.draw.rect(surf, col, r)

    @property
    def type_explosion(self) -> str:
        return self._type

    @property
    def puissance(self) -> float:
        raw = self.sl_puiss.get_current_value()
        lim = TYPES[self._type]
        t   = raw / SLIDER_MAX
        ll  = math.log10(max(lim["min"], 1e-9))
        lh  = math.log10(lim["max"])
        return 10 ** (ll + t * (lh - ll))

    @property
    def altitude(self) -> float:
        return float(self.sl_alt.get_current_value())

    @property
    def taille_monde_m(self) -> float:
        return _slider_log_value(self.sl_monde.get_current_value(), 4000.0, 30000.0)

    @property
    def densite_batiments_km2(self) -> float:
        return float(self.sl_densite_bat.get_current_value())

    @property
    def superficie_batiments(self) -> float:
        return self.sl_superficie.get_current_value() / 100.0

    @property
    def densite(self) -> float:
        if self._rho_auto:
            return densite_atmosphere(self.altitude)
        return self.sl_rho.get_current_value() / 1000.0


def _fmt(v: float) -> str:
    if v >= 1000: return f"{v/1000:.2f}k"
    if v >= 1:    return f"{v:.2f}"
    if v >= 0.001: return f"{v:.4f}"
    return f"{v:.6f}"


def _slider_log_value(raw: float, vmin: float, vmax: float) -> float:
    t = raw / SLIDER_MAX
    ll = math.log10(vmin)
    lh = math.log10(vmax)
    return 10 ** (ll + t * (lh - ll))
