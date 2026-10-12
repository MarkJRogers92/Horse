@tool
extends EditorPlugin


class HorseAssetEvidenceExport extends EditorExportPlugin:
	# Imported resources still use Godot's normal remaps. Runtime SHA checks also
	# need these original files, which a normal resource export omits.
	const SOURCE_FILES = [
		"res://assets/horse/production/horse_light.glb",
		"res://assets/horse/production/horse_light_walk.glb",
		"res://assets/horse/production/horse_stock_walk.glb",
		"res://assets/horse/production/horse_foal_walk.glb",
		"res://assets/horse/production/horse_light_body_detail_v5.png",
		"res://assets/horse/production/evidence/light-v5-detail/ai-neutral-skin-mask.png",
	]

	func _get_name() -> String:
		return "HorseAssetEvidence"

	func _export_file(path: String, _type: String, _features: PackedStringArray) -> void:
		if path in SOURCE_FILES or (path.ends_with(".json") and (
			path.begins_with("res://assets/horse/production/evidence/light-walk-v6-anatomy/") or
			path.begins_with("res://assets/horse/production/evidence/stock-walk-anatomy/") or
			path.begins_with("res://assets/horse/production/evidence/foal-walk-anatomy/") or
			path.begins_with("res://assets/horse/production/evidence/stock-walk-polish/") or
			path.begins_with("res://assets/horse/production/evidence/foal-walk-polish/"))):
			add_file(path, FileAccess.get_file_as_bytes(path), false)


var evidence_export: HorseAssetEvidenceExport


func _enter_tree() -> void:
	evidence_export = HorseAssetEvidenceExport.new()
	add_export_plugin(evidence_export)


func _exit_tree() -> void:
	remove_export_plugin(evidence_export)
	evidence_export = null
