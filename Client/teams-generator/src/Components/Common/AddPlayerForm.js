import { Button, Form, Input } from "antd";
import { useContext } from "react";
import { v4 as uuidv4 } from 'uuid';
import { PlayersContext } from "../../Store/PlayersContext";

function AddPlayerForm(props) {

    const playersContext = useContext(PlayersContext)
    const [form] = Form.useForm();

    const layout = {
        labelCol: { span: 4 },
        wrapperCol: { span: 16 },
    };

    function addPlayerHandler(player) {
        playersContext.setPlayers([...playersContext.players, player])
    }

    const formFinishedHandler = (values) => {
        values['key'] = uuidv4();
        addPlayerHandler(values)
        resetFromHandler();
    };

    const resetFromHandler = () => {
        form.resetFields();
    };

    return (
        <Form {...layout} form={form} onFinish={formFinishedHandler} style={{ marginTop: '15px' }} size="small">

            {props.playersProperties && props.playersProperties.filter(f => f.showInClient).map((property) =>

                <Form.Item name={property.name} label={property.name} rules={[{ required: true }]}>
                    <Input type={property.type} tep={0.1} max={10} min={1} />
                </Form.Item>

            )}

            <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>
                <Button type="primary" htmlType="submit" style={{ marginRight: 16 }}>
                    Add
                </Button>

                <Button htmlType="button" onClick={resetFromHandler}>
                    Reset
                </Button>
            </div>

        </Form>
    )
}

export default AddPlayerForm;