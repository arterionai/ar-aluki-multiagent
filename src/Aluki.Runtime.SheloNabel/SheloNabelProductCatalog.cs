namespace Aluki.Runtime.SheloNabel;

/// <summary>
/// Static Sheló NABEL product catalog embedded for LLM context injection.
/// Includes prices (MXN), doses, product combinations, and contraindications.
/// </summary>
internal static class SheloNabelProductCatalog
{
    internal const string CatalogText =
        """
        ## Catálogo Sheló NABEL (precios MXN, 2025–2026)

        ### Línea Baba de Caracol ⭐ (más vendida)

        **Crema Facial Baba de Caracol 250ml — $289**
        - Ingredientes: alantoína, colágeno, elastina
        - Beneficios: hidrata, atenúa líneas de expresión, manchas, puntos negros y cicatrices
        - Dosis: mañana y noche en rostro limpio, tamaño chícharo; FPS 30 en el día
        - Combina con: Suero Baba de Caracol (potencia efecto), Botox Noche (rutina nocturna), Agua Termal
        - Contraindicaciones: no usar sobre heridas abiertas; prueba en zona pequeña primero
        - Perfil ideal: cualquier tipo de piel desde los 20 años; manchas, acné, cicatrices, piel deshidratada

        **Crema Corporal Baba de Caracol 950ml — $375**
        - Beneficios: hidratación profunda, desvanece cicatrices y manchas del cuerpo
        - Dosis: generosamente en cuerpo limpio, mañana y noche; FPS 30 en zonas expuestas
        - Combina con: Jabón Baba de Caracol, Crema de Manos Uva
        - Perfil: piel seca, cicatrices de cesárea, embarazo o cirugías

        **Suero Baba de Caracol 20ml — $377**
        - Ingredientes: plancton marino, algas azules (250+ componentes), vitaminas, antioxidantes
        - Beneficios: piel más firme, luminosa y elástica; trata arrugas, acné, manchas
        - Dosis: 2 gotas en rostro limpio, mañana y noche, ANTES de la crema
        - Combina con: Crema Facial (aplica suero primero), Agua Termal
        - Contraindicaciones: consultar médico en embarazo; prueba en zona pequeña primero
        - Perfil ideal: mujeres +30 años con líneas de expresión, piel opaca o flácida; es el producto estrella

        **Shampoo Baba de Caracol 530ml — $251**
        - Ingredientes: alantoína, colágeno, elastina
        - Beneficios: nutre cabello seco y frágil, reduce caída
        - Dosis: 2–3 veces/semana en cabello mojado, masajear, enjuagar
        - Combina con: Papa Acondicionador, Ampolletas Hidratantes
        - Perfil: cabello seco, dañado por tinte o calor

        **Jabón Baba de Caracol 100g — $83 | Jabón para Manos 250ml — $153**
        - Uso facial y corporal; microesferas eliminan células muertas
        - Combina con: toda la línea Baba de Caracol como paso de limpieza inicial

        **Kit Familia Baba de Caracol (7 productos) — $1,605**
        - Incluye: Crema Facial + Crema Corporal + Suero + Shampoo + Jabón Facial/Corporal + Jabón Manos + Crema Manos Seda/Vainilla
        - Los 7 productos sueltos superan $1,700 — el Kit es la mejor propuesta de valor
        - Estrategia: si la clienta tiene 3+ productos de la línea, proponer el Kit como upgrade conveniente

        ---

        ### Cuidado Capilar

        **Cabello Sano Shampoo 530ml — $205**
        - Fortalece y nutre desde la raíz
        - Combina con: Papa Acondicionador ($155) + Ampolletas ($251) = combo capilar completo

        **Papa Acondicionador 530ml — $155**
        - Dosis: después del shampoo, 2–3 min, enjuagar; no aplicar en raíz
        - Combina con: cualquier shampoo Sheló NABEL

        **Crema Nutritiva para Peinar 250ml — $135**
        - Sin enjuague; aplicar en cabello húmedo antes de peinar
        - Perfil: cabello rizado, encrespado, o muy seco

        **Hidratante Capilar Ampolletas 10ml — $251**
        - Tratamiento intensivo semanal en cuero cabelludo y longitudes
        - Perfil: cabello muy dañado, con quiebres o puntas abiertas

        **Shampoo de Cebolla 530ml**
        - Antioxidante; estimula crecimiento y reduce caída severa
        - Contraindicaciones: olor fuerte (advertir); no tiñe el cabello
        - Perfil: caída notable, cabello fino

        **Retocador de Canas (lápiz, varios tonos) — $185**
        - Solución temporal entre tinturas; lavable
        - Perfil: clientas que quieren ocultar raíces grises entre visitas al salón

        ---

        ### Cuidado Facial y Cosméticos

        **Botox Noche Nocturno 40gr — $315**
        - Crema regeneradora de uso exclusivamente nocturno
        - Dosis: último paso de la rutina nocturna, cantidad pequeña
        - Combina con: Suero (primero) → Crema Facial (segundo) → Botox Noche (último)
        - Contraindicaciones: solo nocturno, sin FPS — NO usar de día
        - Perfil ideal: mujeres +35 años con líneas marcadas, piel madura

        **Suero de Centella Asiática 30ml**
        - Cierra poros, ilumina, atenúa cicatrices de acné
        - Perfil: acné activo, poros dilatados, tono irregular

        **Mascarilla con Carbón Activado — $199**
        - Limpieza profunda de poros; uso 1–2 veces/semana
        - Contraindicaciones: no usar en piel irritada o rosacea activa

        **Agua Termal 140ml**
        - Fija maquillaje, equilibra e hidrata; como bruma refrescante
        - Combina con: cualquier producto facial

        **Maquillaje BB Baba de Caracol (Natural o Canell) 30ml — $159**
        - Base liviana con beneficio de cuidado de piel + cobertura ligera
        - Perfil: look natural con beneficio adicional

        **Parches Protectores para Granitos (ácido salicílico)**
        - Uso nocturno; reducen inflamación
        - Perfil: acné activo, especialmente adolescentes y veinte-añeras

        **Parches Colágeno Contorno de Ojos (hidrogel)**
        - 1–2 veces/semana; hidratan, reducen ojeras
        - Combina con: Botox Noche, Suero Baba de Caracol

        ---

        ### Cuidado Corporal

        **Crema Reafirmante Corporal 250gr — $259**
        - Reafirma y tonifica; ideal post-pérdida de peso o posparto
        - Perfil: flacidez moderada o tonificación

        **Pomada Árnica y Ocote 140g — $229** ⭐ TOP VENTAS
        - Anti-inflamatoria natural; alivio muscular y articular
        - Dosis: aplicar en zona afectada 2–3 veces/día con masaje
        - Contraindicaciones: no aplicar en heridas abiertas ni piel irritada; uso externo
        - Perfil: adultos con dolor muscular, artritis, contusiones, deportistas; vende bien a hombres y personas mayores

        **Chile Gel 250ml — $109**
        - Efecto calor; alivia dolor muscular y articular
        - Dosis: masajear en zona dolorosa; lavarse manos después
        - Contraindicaciones: NO aplicar cerca de ojos, mucosas ni heridas; evitar en piel sensible
        - Combina con: Pomada Árnica (alternarse o complementarse)

        **Crema Corporal D-B-T 250ml — $225 | Crema de Manos Uva 70gr — $85**
        - Add-on fácil de bajo precio; incluir en todo pedido

        ---

        ### Bienestar / Suplementos

        **Té RDX 20 sobres — $199**
        - 1 sobre en agua caliente al día; bienestar digestivo y detox
        - Perfil: inflamación, digestión lenta

        **Gomitas con Probióticos 150g — $489**
        - 2–3 gomitas al día con alimentos; alta adherencia por formato
        - Combina con: Colágeno Hidrolizado

        **Colágeno Hidrolizado + Omegas 3,6,9**
        - 1 medida diaria en agua, jugo o licuado
        - Combina con: Para Ellas +35, Gomitas Probióticos, productos faciales
        - Perfil: mujeres +35; excelente cross-sell para clientas de línea facial

        **Para Ellas +35 / Para Ellos +35**
        - Soporte hormonal para mayores de 35 años
        - Contraindicaciones: consultar médico si hay terapia hormonal activa o embarazo
        - Perfil: perimenopausia, andropausia, cansancio hormonal
        - Estrategia: mencionar con Colágeno cuando la clienta habla de energía o ciclos

        **Magnesio + Maca**
        - Energía, estado de ánimo, función muscular
        - Perfil: cansancio crónico, calambres, estrés

        **Shelo Transfer Sobres 10gr — $649 | NAD Colágeno + Resveratrol**
        - Productos premium; mayor comisión; perfil clientas +40 enfocadas en longevidad

        **Gomitas Vinagre de Manzana (sabor fresa)**
        - Apoyo metabólico; popular en redes; perfil: bajar de peso, mejorar metabolismo

        **Vita Niños (proteína + probióticos)**
        - Cross-sell cuando la clienta tiene hijos pequeños

        ---

        ### Esencias Aromáticas — $189–199 c/u
        Alegría (cítrico), Optimismo Cítricos, Serenidad (lavanda)
        - Add-on de bajo precio; excelente como detalle con pedido grande

        ### Higiene Personal
        - Pasta Dental con Sábila / Sin Flúor — $171 c/u
        - Gel Antibacterial 70% — $160 | Repelente Mosquitos — $136
        - Desodorante Para Pies — $219

        ---

        ### Rutinas recomendadas (para scripts de venta)

        **Rutina facial básica (starter ~$372):** Jabón Baba + Crema Facial + FPS
        **Rutina facial completa (~$860):** + Suero + Agua Termal
        **Rutina anti-edad nocturna (~$981):** Suero + Crema Facial + Botox Noche
        **Rutina capilar completa (~$611):** Shampoo + Acondicionador Papa + Ampolletas
        **Bienestar femenino +35:** Para Ellas +35 + Colágeno + Gomitas Probióticos
        **Kit estrella:** Kit Familia Baba de Caracol $1,605 (7 productos)

        ### Modelo de negocio
        - Pedido mínimo: $500 MXN + envío ($40 estándar / $55 express CDMX/GDL/MTY)
        - Empresa 100% mexicana. Certificaciones COFEPRIS, INVIMA, FDA, ISO 9001:2015
        - Mercado activo: México, EEUU (comunidad latina), Colombia
        """;
}
