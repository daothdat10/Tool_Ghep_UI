# Tool Ghép UI — Screenshot UI Composer

Tool Unity Editor dựng prefab UGUI từ ảnh tham chiếu, thư mục sprite và font. Có chế độ offline và tùy chọn kết nối API 9Router để phân tích bố cục/chữ hoặc tạo sprite còn thiếu.

## Cài đặt

1. Dùng Unity **2022.3 LTS** (đã kiểm tra với 2022.3.23f1).
2. Project cần UGUI, TextMeshPro và `com.unity.nuget.newtonsoft-json`.
3. Import [ScreenshotUIComposer.unitypackage](Packages/ScreenshotUIComposer.unitypackage), hoặc copy thư mục `Assets/Tools/ScreenshotUIComposer` vào cùng vị trí trong project Unity.
4. Mở **Tools → UI Composer → From Screenshot**.

Giữ nguyên vị trí thư mục tool: OCR đang tìm script tại `Assets/Tools/ScreenshotUIComposer/Editor/UIComposerOcr.ps1.txt`.

## Sử dụng

- Chọn nguồn icon/font một lần (các thư mục bên trong `Assets`), hoặc import thư mục ngoài project.
- Kéo ảnh PNG/JPG vào cửa sổ và bấm **Dựng popup · Offline**.
- Tool xuất prefab và các asset bổ sung vào `Assets/GeneratedUI/`.
- Dùng mục **Chỉnh thêm** để sửa layer, sprite, text và lưu lại prefab.

OCR offline dùng Windows.Media.Ocr có sẵn trên **Windows**; không cần Python. Máy cần có ngôn ngữ OCR Windows. TTF/OTF được chuyển sang TMP font khi cần. Nguồn mặc định `Assets/Sprite` / `Assets/Font` có thể đổi theo project.

### Kết nối 9Router

Bật **Dùng 9Router để hoàn thiện text / UI**, nhập Base URL và API key, rồi tải/chọn model nhận diện ảnh và model tạo ảnh. URL và model được ghi nhớ theo project; API key chỉ giữ trong bộ nhớ hoặc đọc từ biến môi trường `NINEROUTER_KEY`.

Ảnh tham chiếu và danh mục asset được gửi tới endpoint khi bật chế độ API. Model chat cần hỗ trợ ảnh; model tạo ảnh cần hỗ trợ `/v1/images/generations` trả JSON/base64/URL hoặc ảnh đồng bộ. Tool giữ bản offline nếu API thất bại. Danh sách model tải được không đảm bảo provider có quyền phục vụ yêu cầu thực tế.

## Phạm vi và giới hạn

- Kết quả gồm Canvas, Image, TextMeshPro và Button khi nhận diện được; chưa tự suy ra logic gameplay hoặc sự kiện mua hàng.
- Chế độ offline ưu tiên popup game sáng trên nền tối. Asset thiếu được tách/khôi phục từ ảnh mẫu, không sinh chi tiết chưa từng xuất hiện.
- Chế độ AI có thể dựng thêm asset bằng model ảnh; cần đối chiếu hình render và chỉnh lại khi sai.
- Không bảo đảm tự khớp hoàn toàn mọi ảnh. Font, hiệu ứng sáng, alpha và thành phần bị che có thể khác ảnh gốc.
- Nút **Khớp ảnh Shuffle mẫu** là bộ dựng riêng cho mẫu Shuffle đã đối chiếu trong quá trình phát triển. Nó yêu cầu ảnh/bố cục và sprite nguồn tương ứng, không phải bộ dựng chung cho mọi popup.
- Bộ dựng Shuffle giữ chữ trang trí bằng sprite để giữ hình dáng/viền/bóng; nhóm `EditableTextAlternatives` có chữ TMP thay thế, tắt mặc định để không chồng hình.

## Nội dung repository

- `Assets/Tools/ScreenshotUIComposer/Editor`: mã nguồn C# và script OCR Windows.
- `Packages/ScreenshotUIComposer.unitypackage`: gói cài Unity.
- [Hướng dẫn chi tiết](Assets/Tools/ScreenshotUIComposer/README.md).

Repository chỉ chứa tool ghép UI; không chứa project Bus, icon/font riêng của project, dữ liệu tài khoản/API key hay server Local AI Gateway.

## Đã kiểm tra

Biên dịch và chạy qua Unity MCP trong Unity 2022.3.23f1; xuất/mở lại prefab, tham chiếu sprite/font, OCR Windows và render đối chiếu mẫu. Luồng API đã kiểm tra bằng máy chủ mô phỏng; lỗi provider/quyền tài khoản được trả riêng. Bộ dựng Shuffle đã render và kiểm tra các lớp artwork/TMP thay thế.
