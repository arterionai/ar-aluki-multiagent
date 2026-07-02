namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Static Sheló NABEL product catalog embedded for LLM context injection.
/// Includes prices (MXN), doses, product combinations, and contraindications.
/// Dense one-line-per-product format: this text is re-sent on EVERY message, so
/// token weight directly affects reply latency — keep it compact, keep every
/// product/price/dose/contraindication fact.
/// </summary>
internal static class SheloNabelProductCatalog
{
    internal const string CatalogText =
        """
        ## Catálogo Sheló NABEL (precios MXN 2025–2026; formato: producto — precio | beneficio | dosis | combos | precauciones | perfil)

        ### Línea Baba de Caracol ⭐ (más vendida)
        - Crema Facial Baba de Caracol 250ml — $289 | alantoína+colágeno+elastina; hidrata, atenúa líneas, manchas, puntos negros, cicatrices | tamaño chícharo mañana y noche en rostro limpio, FPS 30 de día | combos: Suero Baba (antes), Botox Noche, Agua Termal | no sobre heridas abiertas, probar en zona pequeña | perfil: 20+, cualquier piel; manchas/acné/cicatrices/deshidratación
        - Crema Corporal Baba de Caracol 950ml — $375 | hidratación profunda, desvanece cicatrices y manchas corporales | generosa mañana y noche, FPS 30 en zonas expuestas | combos: Jabón Baba, Crema de Manos Uva | perfil: piel seca, cicatrices de cesárea/embarazo/cirugías
        - Suero Baba de Caracol 20ml — $377 ⭐ producto estrella | plancton marino + algas azules (250+ componentes); firmeza, luminosidad, elasticidad; arrugas/acné/manchas | 2 gotas mañana y noche ANTES de la crema | combos: Crema Facial (después), Agua Termal | consultar médico en embarazo, probar en zona pequeña | perfil: mujeres 30+ con líneas, piel opaca o flácida
        - Shampoo Baba de Caracol 530ml — $251 | nutre cabello seco/frágil, reduce caída | 2–3 veces/semana | combos: Papa Acondicionador, Ampolletas | perfil: cabello dañado por tinte o calor
        - Jabón Baba de Caracol 100g — $83 · Jabón para Manos 250ml — $153 | facial y corporal, microesferas exfoliantes | paso de limpieza inicial de toda la línea
        - Kit Familia Baba de Caracol (7 productos) — $1,605 | Crema Facial + Corporal + Suero + Shampoo + Jabón Facial/Corporal + Jabón Manos + Crema Manos Seda/Vainilla; sueltos >$1,700 | estrategia: si la clienta tiene 3+ de la línea, proponer el Kit como upgrade

        ### Cuidado Capilar
        - Cabello Sano Shampoo 530ml — $205 | fortalece desde la raíz | combo capilar completo: + Papa Acondicionador $155 + Ampolletas $251
        - Papa Acondicionador 530ml — $155 | tras el shampoo 2–3 min, enjuagar, no en raíz
        - Crema Nutritiva para Peinar 250ml — $135 | sin enjuague, en cabello húmedo | perfil: rizado/encrespado/muy seco
        - Hidratante Capilar Ampolletas 10ml — $251 | tratamiento intensivo semanal | perfil: cabello muy dañado, puntas abiertas
        - Shampoo de Cebolla 530ml | antioxidante, estimula crecimiento, reduce caída severa | advertir olor fuerte; no tiñe | perfil: caída notable, cabello fino
        - Retocador de Canas (lápiz, varios tonos) — $185 | temporal entre tinturas, lavable | perfil: raíces grises entre visitas al salón

        ### Cuidado Facial y Cosméticos
        - Botox Noche Nocturno 40gr — $315 | regeneradora | último paso nocturno, cantidad pequeña | orden: Suero → Crema Facial → Botox Noche | SOLO nocturno, sin FPS, NO de día | perfil: 35+, líneas marcadas, piel madura
        - Suero de Centella Asiática 30ml | cierra poros, ilumina, atenúa cicatrices de acné | perfil: acné activo, poros dilatados, tono irregular
        - Mascarilla con Carbón Activado — $199 | limpieza profunda de poros, 1–2 veces/semana | no en piel irritada o rosácea activa
        - Agua Termal 140ml | fija maquillaje, equilibra, hidrata; bruma refrescante | combina con cualquier facial
        - Maquillaje BB Baba de Caracol (Natural o Canell) 30ml — $159 | base liviana, cobertura ligera + cuidado de piel | perfil: look natural
        - Parches Protectores para Granitos (ácido salicílico) | nocturno, reducen inflamación | perfil: acné activo, adolescentes/veinteañeras
        - Parches Colágeno Contorno de Ojos (hidrogel) | 1–2 veces/semana, hidratan, reducen ojeras | combos: Botox Noche, Suero Baba

        ### Cuidado Corporal
        - Crema Reafirmante Corporal 250gr — $259 | reafirma y tonifica | perfil: flacidez post-pérdida de peso o posparto
        - Pomada Árnica y Ocote 140g — $229 ⭐ TOP VENTAS | anti-inflamatoria natural, alivio muscular/articular | 2–3 veces/día con masaje | no en heridas abiertas ni piel irritada, uso externo | perfil: dolor muscular, artritis, contusiones, deportistas; vende bien a hombres y mayores
        - Chile Gel 250ml — $109 | efecto calor, dolor muscular/articular | masajear zona, lavarse las manos después | NO cerca de ojos/mucosas/heridas, evitar piel sensible | combo: alternar con Pomada Árnica
        - Crema Corporal D-B-T 250ml — $225 · Crema de Manos Uva 70gr — $85 | add-on de bajo precio, incluir en todo pedido

        ### Bienestar / Suplementos
        - Té RDX 20 sobres — $199 | digestivo y detox, 1 sobre/día en agua caliente | perfil: inflamación, digestión lenta
        - Gomitas con Probióticos 150g — $489 | 2–3/día con alimentos, alta adherencia | combo: Colágeno Hidrolizado
        - Colágeno Hidrolizado + Omegas 3,6,9 | 1 medida diaria en agua/jugo/licuado | combos: Para Ellas +35, Gomitas Probióticos, línea facial | perfil: mujeres 35+, cross-sell de clientas faciales
        - Para Ellas +35 / Para Ellos +35 | soporte hormonal 35+ | consultar médico si hay terapia hormonal o embarazo | perfil: perimenopausia/andropausia/cansancio; mencionar con Colágeno cuando hablan de energía o ciclos
        - Magnesio + Maca | energía, ánimo, función muscular | perfil: cansancio crónico, calambres, estrés
        - Shelo Transfer Sobres 10gr — $649 · NAD Colágeno + Resveratrol | premium, mayor comisión | perfil: clientas 40+ longevidad
        - Gomitas Vinagre de Manzana (fresa) | apoyo metabólico, popular en redes | perfil: bajar de peso
        - Vita Niños (proteína + probióticos) | cross-sell con hijos pequeños

        ### Esencias Aromáticas — $189–199 c/u
        - Alegría (cítrico), Optimismo Cítricos, Serenidad (lavanda) | add-on de bajo precio, detalle con pedido grande

        ### Higiene Personal
        - Pasta Dental con Sábila / Sin Flúor — $171 c/u · Gel Antibacterial 70% — $160 · Repelente Mosquitos — $136 · Desodorante Para Pies — $219

        ### Rutinas recomendadas (scripts de venta)
        - Facial básica ~$372: Jabón Baba + Crema Facial + FPS
        - Facial completa ~$860: + Suero + Agua Termal
        - Anti-edad nocturna ~$981: Suero + Crema Facial + Botox Noche
        - Capilar completa ~$611: Shampoo + Acondicionador Papa + Ampolletas
        - Bienestar femenino +35: Para Ellas +35 + Colágeno + Gomitas Probióticos
        - Kit estrella: Kit Familia Baba de Caracol $1,605 (7 productos)

        ### Modelo de negocio
        - Pedido mínimo $500 MXN + envío ($40 estándar / $55 express CDMX/GDL/MTY). Empresa 100% mexicana; COFEPRIS, INVIMA, FDA, ISO 9001:2015. Mercado: México, EEUU (comunidad latina), Colombia.
        """;
}
