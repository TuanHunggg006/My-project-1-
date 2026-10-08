# Thay đổi gameplay

Mở `Assets/Scenes/Main.unity` và bấm Play. HUD điểm được tạo tự động; không cần gán thêm tham chiếu trong Inspector.

## Khối

Danh sách 11 mẫu, tổng cộng 32 hướng xoay/lật: ô đơn, thanh 2–5 ô, vuông 2×2, chữ nhật đầy đủ 2×3/3×2, góc 3 ô, L 4 ô, T 4 ô và S/Z 4 ô. Loại bỏ L 5 ô và khối 5 ô thiếu góc như ảnh tham khảo. Các khối lớn được giảm khi bảng gần đầy; mọi gợi ý vẫn phải đặt được trên bảng hiện tại.

## Điểm mỗi lượt đặt

Chỉ cộng điểm khi hoàn thành hàng/cột. Tính đồng thời tất cả đường trước khi xóa; ô giao nhau chỉ bị xóa một lần.

`Điểm = số đường × 100 × (1 + floor(số đường / 2))`.

| Số đường | Hệ số | Điểm |
| --- | --- | --- |
| 1 | x1 | 100 |
| 2 | x2 | 400 |
| 3 | x2 | 600 |
| 4 | x3 | 1.200 |
| 5 | x3 | 1.500 |
| 6 | x4 | 2.400 |

Hàng và cột có cùng giá trị: 1 ngang + 1 dọc và 2 đường song song đều là 400 điểm; 2 ngang + 2 dọc là 1.200 điểm. Hệ số tính theo một lượt đặt, không phải chuỗi các lượt. Điểm cao nhất được lưu với khóa PlayerPrefs `BlockPuzzle.BestScore`.

## Kéo thả và hiệu ứng

Khối phóng to trong 0,10 giây, bám vị trí ngón tay/chuột và được hút nhẹ về ô hợp lệ. Bóng gợi ý dùng cùng phép kiểm tra với lúc thả. Đặt đúng có nhịp đáp 0,12 giây; đặt sai quay về khay trong 0,18 giây. Khi kéo vào vị trí hợp lệ có thể hoàn thành hàng/cột, cả đường đổi sang màu của khối kéo và sáng ngay, trước khi thả. Kéo sang vị trí khác hoặc hủy kéo sẽ tắt ánh sáng; gợi ý không thay đổi ô đã chiếm hay cộng điểm.

Khi thả đúng, đường tiếp tục sáng trong nhịp đáp, lóe trong 0,09 giây rồi các ô mờ nhanh và tan thành hạt vuông nhỏ cùng màu trong 0,36 giây. Quầng sáng màu tắt dần; đã bỏ phồng/xoay ô và quầng trắng lớn. Các hạt dùng chung một mesh UI trong `BlockClearSparks`; gợi ý sáng được tái sử dụng khi kéo. Hàng/cột giao nhau dùng chung các ô hiệu ứng. HUD đếm điểm có nhịp nảy và quầng sáng tím; điểm cộng/Combo nổi lên trong bảng.

Các tham số có thể chỉnh trong `GridDropBoard`, `DraggableGeneratedShape` và `BlockPuzzleScoreDisplay`. Điểm số và HUD không dùng chung với dòng thông báo số khối còn lại.

## Kiểm tra

`Tools > Block Puzzle > Validate shapes and scoring` kiểm tra danh sách khối và quy tắc điểm trong Unity. `BlockPuzzleValidation.RunBatch` dành cho bản sao kiểm thử bằng dòng lệnh; phương thức này kết thúc tiến trình Editor sau khi chạy. Các kiểm tra Play Mode bao gồm mọi hướng khối/vị trí neo, ô đã chiếm, biên bảng, bóng đặt, kéo/thả, giao điểm, combo 4 đường, trả khối và lưu kỷ lục.

Bản code/scene trước thay đổi nằm trong `Development/BeforeChanges`.

Kết quả: biên dịch thành công trên Unity 2022.2.23f1; 304 kiểm tra quy tắc và 18.185 kiểm tra Play Mode đạt. Console của project chính không có lỗi, scene Main đã nhận `maximumShapeSize = 6`, `pointsPerLine = 100` và `clearRotation = 0`. Kết quả chi tiết nằm trong `Development/Validation/results.txt`; ảnh hiệu ứng là `Development/Validation/four-line-combo.png`.

Lần sửa hiệu ứng: biên dịch và chạy trực tiếp trong Unity Editor đang mở; 18.196 kiểm tra đạt, gồm gợi ý giao nhau 15/28 ô, chuyển vị trí/hủy gợi ý, không cộng điểm khi kéo, mesh 45 hạt có CanvasRenderer, hiệu ứng kết thúc và trả khối đặt sai. Editor được đưa về Edit Mode, không thay đổi kỷ lục của người chơi. Kết quả: `Development/Validation/effects-results.txt`; ảnh mới có tiền tố `effects-`. Có thể chạy `BlockPuzzleValidation.RunConnected()` trong Edit Mode để kiểm tra scene hiện tại mà không đóng Editor. Bản sao trước lần sửa hiệu ứng ở `Development/BeforeEffectRevision`.
